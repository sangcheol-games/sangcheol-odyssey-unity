using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SCOdyssey.Game;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.Data
{
    /// <summary>
    /// EditorChartData ↔ 채보 텍스트 변환, EditorChartData → ChartData 변환 (프리뷰용)
    /// </summary>
    public static class EditorChartConverter
    {
        #region EditorChartData → 채보 텍스트 (저장)

        /// <summary>
        /// EditorChartData를 채보 텍스트 형식으로 변환
        /// </summary>
        public static string ToChartText(EditorChartData data)
        {
            StringBuilder sb = new StringBuilder();

            // 마디 번호 순으로 정렬
            var sortedBars = data.GetAllBars()
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value);

            foreach (var bar in sortedBars)
            {
                for (int laneIdx = 0; laneIdx < 4; laneIdx++)
                {
                    int laneNumber = laneIdx + 1;

                    // 방향이 설정되지 않은 레인은 출력하지 않음
                    if (!bar.IsDirectionSet(laneNumber)) continue;

                    bool isLTR = bar.GetDirection(laneNumber);
                    bool hasNotes = bar.HasAnyNote(laneIdx);

                    // 방향은 설정됐지만 노트가 없는 경우 → 빈 시퀀스 출력 (판정선은 이동)
                    // 방향 미설정 + 노트 없음 → 출력하지 않음 (위에서 continue)

                    // 채널 계산: channel = direction(0/1) + lane(1-4)
                    int channel = isLTR ? 0 : 1;
                    string channelLane = $"{channel}{laneNumber}";

                    // 시퀀스 문자열 생성 (화면 왼→오 순서 그대로)
                    string sequence = new string(bar.laneSequences[laneIdx], 0, bar.beat);

                    // #barNumber:channelLane:sequence;
                    sb.AppendLine($"#{bar.barNumber:D3}:{channelLane}:{sequence};");
                }
            }

            return sb.ToString();
        }

        #endregion

        #region 채보 텍스트 → EditorChartData (불러오기)

        /// <summary>
        /// 채보 텍스트를 EditorChartData로 변환
        /// </summary>
        public static EditorChartData FromChartText(string chartText, int bpm)
        {
            EditorChartData data = new EditorChartData();
            data.bpm = bpm;

            string[] lines = chartText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                if (!line.StartsWith("#") || !line.EndsWith(";")) continue;

                try
                {
                    string content = line.TrimStart('#').TrimEnd(';');
                    string[] parts = content.Split(':');

                    if (parts.Length < 3) continue;

                    int barNumber = int.Parse(parts[0]);

                    string channelLaneStr = parts[1];
                    int channel = channelLaneStr[0] - '0';
                    int laneNumber = channelLaneStr[1] - '0';

                    bool isLTR = (channel == 0);
                    string noteSequence = parts[2];
                    int beat = noteSequence.Length;

                    // 마디 데이터 가져오기 (없으면 생성)
                    EditorBarData bar = data.GetOrCreateBar(barNumber);

                    // 해당 마디의 비트를 가장 큰 값으로 갱신
                    if (beat > bar.beat)
                    {
                        bar.SetBeat(beat);
                    }

                    // 방향 설정
                    int groupIndex = (laneNumber <= 2) ? 0 : 1;
                    if (groupIndex == 0)
                        bar.upperGroupLTR = isLTR;
                    else
                        bar.lowerGroupLTR = isLTR;

                    // 시퀀스 저장 (화면 순서 그대로 — Reverse 불필요)
                    int laneIdx = laneNumber - 1;
                    for (int i = 0; i < beat && i < bar.laneSequences[laneIdx].Length; i++)
                    {
                        bar.laneSequences[laneIdx][i] = noteSequence[i];
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EditorChartConverter] Parse error at line: {line}\n{e.Message}");
                }
            }

            return data;
        }

        #endregion

        #region EditorChartData → ChartData (프리뷰용)

        /// <summary>
        /// EditorChartData 전체를 게임용 ChartData로 변환 (프리뷰 재생용)
        /// </summary>
        public static ChartData ToPlayableChartData(EditorChartData data)
        {
            return ToPlayableChartData(data, 0, data.GetLastBarNumber());
        }

        /// <summary>
        /// 지정 범위의 마디만 게임용 ChartData로 변환 (부분 프리뷰용)
        /// </summary>
        public static ChartData ToPlayableChartData(EditorChartData data, int startBar, int endBar)
        {
            ChartData chartData = new ChartData();
            chartData.bpm = data.bpm;

            double barDuration = data.GetBarDuration();

            for (int barNum = startBar; barNum <= endBar; barNum++)
            {
                EditorBarData bar = data.GetBar(barNum);
                if (bar == null) continue;

                for (int laneIdx = 0; laneIdx < 4; laneIdx++)
                {
                    int laneNumber = laneIdx + 1;
                    if (!bar.IsDirectionSet(laneNumber)) continue;

                    bool isLTR = bar.GetDirection(laneNumber);
                    int beat = bar.beat;
                    double laneStartTime = barNum * barDuration;

                    // 시퀀스 문자열 생성
                    string sequence = new string(bar.laneSequences[laneIdx], 0, beat);

                    // LaneData 생성 (ChartParser와 동일한 흐름)
                    LaneData laneData = new LaneData(barNum, laneStartTime, beat, isLTR, laneNumber);
                    laneData.ConvertSequenceToNotes(sequence, barDuration);

                    chartData.AddLane(laneData);
                }
            }

            return chartData;
        }

        #endregion
    }
}
