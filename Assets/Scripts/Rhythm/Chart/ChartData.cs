using System.Collections.Generic;
using System.Linq;
using static SCOdyssey.Domain.Service.Constants;


namespace SCOdyssey.Rhythm
{
    // 파싱이 끝난 한 곡·난이도의 채보 전체. PlayfieldView.Init이 GetFullChartList()로 받아 BarStreamer에 싣는다.
    // 개별 노트의 판정 시각은 파싱 단계(ChartParser/LaneData)에서 이미 계산되어 들어 있다.
    public class ChartData
    {
        public int bpm;                    // 곡 BPM. barDuration 계산 근거
        public int totalNotes;             // 판정 대상 노트 수(탭·머리·꼬리, 합성 꼬리 포함). ScoreManager 기본점수 계산에 사용
        private List<LaneData> chart;      // 마디×레인 단위 LaneData 목록(파싱 순서 = 마디 순서)

        public ChartData()
        {
            chart = new List<LaneData>();
        }

        public void AddLane(LaneData laneData)
        {
            chart.Add(laneData);
        }

        // 전체 LaneData 목록 반환(채보 파일 순서. 뷰는 마디 순으로 적혀 있다고 보고 앞에서부터 꺼낸다).
        public List<LaneData> GetFullChartList()
        {
            return chart;
        }

        // flat 트랙, 원본에서 시간 오름차순으로 (정렬된 인덱스 = noteId).
        // 본체(3)는 판정 대상이 아니라 빠지고(id -1 그대로), 레인별 시간순으로 머리와 다음 꼬리를 짝짓는다.
        public JudgeNote[] BuildJudgeTrack(ChartParseReport report = null)
        {
            var pairs = new List<(NoteData data, JudgeNote judge)>(totalNotes > 0 ? totalNotes : 256);

            foreach (LaneData laneData in chart)
            {
                Lane lane = LaneMap.FromChartLine(laneData.line);

                if ((int)lane < 0 || (int)lane >= LANE_COUNT)
                {
                    report?.Errors.Add($"채보 레인 번호가 범위를 벗어남: bar={laneData.bar}, line={laneData.line}. 이 레인은 판정 트랙에서 제외한다.");
                    continue;
                }

                foreach (NoteData note in laneData.Notes)
                {
                    note.id = -1;
                    if (NoteKinds.TryFrom(note.noteType, out NoteKind kind))
                        pairs.Add((note, new JudgeNote(note.time, lane, kind)));
                }
            }

            // (참고) 파싱 순서는 "마디 → 그 마디의 레인들" 이라 시간 순이 아님.
            var sorted = pairs
                .OrderBy(p => p.judge.Time) // 시간 순 정렬
                .ThenBy(p => (int)p.judge.Lane)
                .ToArray();

            var track = new JudgeNote[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                sorted[i].data.id = i;   // 뷰가 판정 결과를 되찾아올 열쇠
                track[i] = sorted[i].judge;
            }

            PairHolds(track, report);

            if (report != null) report.TrackNotes = track.Length;
            return track;
        }

        private static void PairHolds(JudgeNote[] track, ChartParseReport report)
        {
            var open = new int[LANE_COUNT];
            for (int lane = 0; lane < LANE_COUNT; lane++) open[lane] = -1;

            for (int i = 0; i < track.Length; i++)
            {
                int lane = (int)track[i].Lane;
                switch (track[i].Kind)
                {
                    case NoteKind.HoldHead:
                        if (open[lane] >= 0) report?.Warnings.Add($"꼬리 없는 홀드 머리: {track[open[lane]]} (탭처럼 판정)");
                        open[lane] = i;
                        break;

                    case NoteKind.HoldTail:
                        if (open[lane] < 0)
                        {
                            report?.Warnings.Add($"머리 없는 홀드 꼬리: {track[i]} (누를 방법이 없어 Miss가 된다)");
                            break;
                        }
                        track[open[lane]] = track[open[lane]].WithPair(i);
                        track[i] = track[i].WithPair(open[lane]);
                        open[lane] = -1;
                        break;
                }
            }

            for (int lane = 0; lane < LANE_COUNT; lane++)
                if (open[lane] >= 0) report?.Warnings.Add($"꼬리 없는 홀드 머리: {track[open[lane]]} (탭처럼 판정)");
        }
    }
}
