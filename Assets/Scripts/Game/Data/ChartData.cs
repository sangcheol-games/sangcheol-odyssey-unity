using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;


namespace SCOdyssey.Game
{
    // 파싱이 끝난 한 곡·난이도의 채보 전체. ChartManager.Init이 GetFullChartList()로 받아 remainingChart에 적재한다.
    // 개별 노트의 판정 시각은 파싱 단계(ChartParser/LaneData)에서 이미 계산되어 들어 있다.
    public class ChartData
    {
        public int bpm;                    // 곡 BPM. barDuration 계산 근거
        public int totalNotes;             // 총 노트 수(헤더 #NOTES). ScoreManager 기본점수 계산에 사용
        private List<LaneData> chart;      // 마디×레인 단위 LaneData 목록(파싱 순서 = 마디 순서)

        public ChartData()
        {
            chart = new List<LaneData>();
        }

        public void AddLane(LaneData laneData)
        {
            chart.Add(laneData);
        }

        // 전체 LaneData 목록 반환. ChartManager가 이 목록으로 Queue<LaneData> remainingChart를 만든다.
        public List<LaneData> GetFullChartList()
        {
            return chart;
        }

        // flat 트랙, 원본에서 시간 오름차순으로 (정렬된 인덱스 = noteId)
        // Holding/HoldEnd/HoldRelease도 전부 일반 원소로 담는다.
        public JudgeNote[] BuildJudgeTrack()
        {
            var flat = new List<JudgeNote>(totalNotes > 0 ? totalNotes : 256);

            foreach (LaneData laneData in chart)
            {
                Lane lane = LaneMap.FromChartLine(laneData.line);

                if ((int)lane < 0 || (int)lane >= LANE_COUNT)
                {
                    Debug.LogError($"채보 레인 번호가 범위를 벗어남: bar={laneData.bar}, line={laneData.line}. 이 레인은 판정 트랙에서 제외한다.");
                    continue;
                }

                foreach (NoteData note in laneData.Notes)
                {
                    flat.Add(new JudgeNote(note.time, lane, note.noteType));
                }
            }

            // (참고) 파싱 순서는 "마디 → 그 마디의 레인들" 이라 시간 순이 아님.
            return flat
                .OrderBy(n => n.Time) // 시간 순 정렬
                .ThenBy(n => (int)n.Lane)
                .ToArray();
        }
    }
}
