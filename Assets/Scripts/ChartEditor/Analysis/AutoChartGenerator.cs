using System.Collections.Generic;
using SCOdyssey.ChartEditor.Data;
using UnityEngine;

namespace SCOdyssey.ChartEditor.Analysis
{
    /// <summary>
    /// onset 감지 결과를 EditorChartData로 변환하는 자동 채보 생성기.
    /// onset 타이밍을 마디/비트에 스냅하고 4개 레인에 분배.
    /// </summary>
    public static class AutoChartGenerator
    {
        /// <summary>
        /// 자동 채보 생성
        /// </summary>
        /// <param name="chartData">기존 EditorChartData (bpm 사용, 내용 덮어쓰기)</param>
        /// <param name="onsets">감지된 onset 목록</param>
        /// <param name="beatResolution">비트 분할수 (기본 8)</param>
        /// <param name="alternating">true: 마디별 상단/하단 교대 배치 (Easy/Normal용)</param>
        public static void Generate(EditorChartData chartData, List<AudioOnsetDetector.OnsetInfo> onsets,
            int beatResolution = 8, bool alternating = false)
        {
            if (onsets == null || onsets.Count == 0)
            {
                Debug.LogWarning("[AutoChartGenerator] No onsets to generate chart from");
                return;
            }

            double barDuration = chartData.GetBarDuration();

            // 0번 마디 제외 (준비 마디), 레인 카운터 초기화
            int laneCounter = 0;
            int notesPlaced = 0;
            int notesSkipped = 0;

            foreach (var onset in onsets)
            {
                // 0번 마디(준비 마디)는 노트 배치 불가
                if (onset.time < barDuration) continue;

                // 마디 번호 계산
                int barNumber = (int)(onset.time / barDuration);

                // 마디 내 상대 위치 → 비트 인덱스로 스냅
                double relativeTime = onset.time - barNumber * barDuration;
                int beatIndex = Mathf.RoundToInt((float)(relativeTime / barDuration * beatResolution));
                beatIndex = Mathf.Clamp(beatIndex, 0, beatResolution - 1);

                // 마디 데이터 가져오기 (없으면 생성)
                EditorBarData bar = chartData.GetOrCreateBar(barNumber);

                // 비트 분할수 맞추기
                if (bar.beat != beatResolution)
                    bar.SetBeat(beatResolution);

                // 방향 자동 설정 (미설정 시)
                if (alternating)
                    EnsureDirectionsAlternating(bar);
                else
                    EnsureDirections(bar);

                // 레인 분배 (라운드 로빈 + 경계 체크)
                int laneIndex = alternating
                    ? GetNextValidLaneAlternating(bar, beatIndex, beatResolution, ref laneCounter)
                    : GetNextValidLane(bar, beatIndex, beatResolution, ref laneCounter);

                if (laneIndex < 0)
                {
                    notesSkipped++;
                    continue;
                }

                // 빈 위치에만 배치
                if (bar.laneSequences[laneIndex][beatIndex] == '0')
                {
                    bar.laneSequences[laneIndex][beatIndex] = '1'; // NoteType.Normal
                    notesPlaced++;
                }
                else
                {
                    notesSkipped++;
                }
            }

            Debug.Log($"[AutoChartGenerator] Generated: {notesPlaced} notes placed, {notesSkipped} skipped, BPM={chartData.bpm}, beat={beatResolution}, alternating={alternating}");
        }

        #region 방향 설정

        /// <summary>
        /// 동시 진행 모드: 상단 + 하단 모두 방향 설정 (Hard/Extreme)
        /// 홀수 마디: 상단 LTR + 하단 RTL, 짝수 마디: 상단 RTL + 하단 LTR
        /// </summary>
        private static void EnsureDirections(EditorBarData bar)
        {
            if (!bar.upperGroupLTR.HasValue)
                bar.upperGroupLTR = (bar.barNumber % 2 == 1);  // 홀수=LTR, 짝수=RTL

            if (!bar.lowerGroupLTR.HasValue)
                bar.lowerGroupLTR = !bar.upperGroupLTR.Value;  // 상단 반대 방향
        }

        /// <summary>
        /// 교대 진행 모드: 홀수 마디→상단(LTR)만, 짝수 마디→하단(RTL)만 (Easy/Normal)
        /// </summary>
        private static void EnsureDirectionsAlternating(EditorBarData bar)
        {
            bool isUpperBar = (bar.barNumber % 2 == 1); // 홀수=상단, 짝수=하단

            if (isUpperBar)
            {
                if (!bar.upperGroupLTR.HasValue)
                    bar.upperGroupLTR = true;  // LTR
                // 하단은 미설정 유지 (null)
            }
            else
            {
                if (!bar.lowerGroupLTR.HasValue)
                    bar.lowerGroupLTR = false; // RTL
                // 상단은 미설정 유지 (null)
            }
        }

        #endregion

        #region 레인 분배

        /// <summary>
        /// 동시 진행 모드: 4개 레인 라운드 로빈 (Hard/Extreme)
        /// </summary>
        private static int GetNextValidLane(EditorBarData bar, int beatIndex, int beatResolution, ref int laneCounter)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int laneIndex = laneCounter % 4;
                int laneNumber = laneIndex + 1;
                laneCounter++;

                // 방향 미설정 레인은 스킵
                if (!bar.IsDirectionSet(laneNumber)) continue;

                bool isLTR = bar.GetDirection(laneNumber);

                // 경계 체크: LTR은 마지막 비트 불가, RTL은 첫 비트 불가
                if (isLTR && beatIndex >= beatResolution) continue;
                if (!isLTR && beatIndex <= 0) continue;

                // 이미 노트가 있으면 다음 레인 시도
                if (bar.laneSequences[laneIndex][beatIndex] != '0') continue;

                return laneIndex;
            }

            return -1;
        }

        /// <summary>
        /// 교대 진행 모드: 활성 그룹의 2개 레인만 사용 (Easy/Normal)
        /// 홀수 마디→레인 1,2 / 짝수 마디→레인 3,4
        /// </summary>
        private static int GetNextValidLaneAlternating(EditorBarData bar, int beatIndex, int beatResolution, ref int laneCounter)
        {
            bool isUpperBar = (bar.barNumber % 2 == 1);
            int startLane = isUpperBar ? 0 : 2; // 상단: 0,1 / 하단: 2,3

            for (int attempt = 0; attempt < 2; attempt++)
            {
                int laneIndex = startLane + (laneCounter % 2);
                int laneNumber = laneIndex + 1;
                laneCounter++;

                if (!bar.IsDirectionSet(laneNumber)) continue;

                bool isLTR = bar.GetDirection(laneNumber);

                if (isLTR && beatIndex >= beatResolution) continue;
                if (!isLTR && beatIndex <= 0) continue;

                if (bar.laneSequences[laneIndex][beatIndex] != '0') continue;

                return laneIndex;
            }

            return -1;
        }

        #endregion
    }
}
