using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 채보 텍스트 → ChartData 변환기. 여기서 마디/노트의 모든 시간을 미리 계산해 넣는다.
    // (런타임에는 시간을 다시 계산하지 않고 이 값을 그대로 판정에 쓴다.)
    public static class ChartParser
    {

        /// <summary>
        /// 채보 텍스트를 파싱해 ChartData를 만든다(GameDataLoader가 호출).
        /// 헤더(#KEY value)와 데이터(#마디:채널레인:시퀀스;)를 구분해 처리하고,
        /// 마디 시작 시각·노트 판정 시각을 모두 계산해 채운다.
        /// 오류와 합성 개수는 report에 담기고, 로그 출력은 호출자가 한다.
        /// </summary>
        public static ChartData Parse(string chartText, int bpm, ChartParseReport report = null)
        {
            ChartData chartData = new ChartData();
            chartData.bpm = bpm;

            // 1. 줄 단위로 나누기 (윈도우/맥/리눅스 개행문자 대응)
            string[] lines = chartText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            double duration = BarClock.FromBpm(bpm).BarDuration;   // 마디 길이(4/4)

            foreach (string line in lines)
            {
                if (!line.StartsWith("#")) continue;

                // 헤더 라인 파싱 (#KEY value)
                if (!line.Contains(':') || !line.EndsWith(";"))
                {
                    ParseHeaderField(line, chartData);
                    continue;
                }

                try
                {
                    // 데이터 파싱: #001:02:01020020; -> 001, 02, 01020020
                    string content = line.TrimStart('#').TrimEnd(';');
                    string[] parts = content.Split(':');

                    if (parts.Length < 3) continue;

                    // 마디 정보
                    int barNumber = int.Parse(parts[0]);

                    // 채널 및 레인 정보
                    string channelLaneStr = parts[1];
                    int channel = channelLaneStr[0] - '0'; // char -> int 변환
                    int lane = channelLaneStr[1] - '0';

                    bool isLTR = (channel == 0);

                    // 노트 데이터 정보
                    string noteSequence = parts[2];
                    int beat = noteSequence.Length;

                    // 마디 시작 시간 계산: 마디번호 * 마디당 시간
                    double laneStartTime = barNumber * duration;

                    // LaneData 생성
                    LaneData laneData = new LaneData(barNumber, laneStartTime, beat, isLTR, lane);

                    // 개별 노트 생성
                    laneData.ConvertSequenceToNotes(noteSequence, duration);

                    // 파싱된 레인 데이터를 차트에 추가
                    chartData.AddLane(laneData);
                }
                catch (System.Exception e)
                {
                    report?.Errors.Add($"Chart Parsing Error at line: {line}\n{e.Message}");
                }
            }

            int headerNotes = chartData.totalNotes;
            int synthesized = AppendBarEndHoldEnds(chartData, duration);
            chartData.totalNotes = CountJudgeable(chartData);   // 헤더 #NOTES는 본체(3)까지 세므로 쓰지 않는다

            if (report != null)
            {
                report.HeaderNotes = headerNotes;
                report.Synthesized = synthesized;
            }

            return chartData;
        }

        /// <summary>
        /// 마디 끝에서 끝나는 홀드에 판정용 종료 노트를 붙인다.
        ///
        /// 시퀀스 한 칸은 마디를 beat등분한 시작점(0/beat ~ (beat-1)/beat)만 가리킬 수 있어
        /// 마디 끝(beat/beat)을 표현할 수 없다. 그래서 채보는 "마디 끝까지 홀드"를 종료 문자의
        /// 생략으로 표현해 왔는데, 그러면 holdBarBeats로 홀드바 길이만 늘어나고 판정 대상은 하나도 안 남는다.
        ///
        /// 다음 마디가 같은 레인을 Holding으로 이어받으면 홀드가 진짜로 계속되는 것이므로 건너뛴다.
        /// </summary>
        private static int AppendBarEndHoldEnds(ChartData chartData, double duration)
        {
            List<LaneData> lanes = chartData.GetFullChartList();

            var byBarLine = new Dictionary<(int bar, int line), LaneData>();
            foreach (LaneData lane in lanes)
                byBarLine[(lane.bar, lane.line)] = lane;

            int added = 0;

            foreach (LaneData lane in lanes)
            {
                if (!lane.holdRunsToBarEnd) continue;
                if (ContinuesInNextBar(byBarLine, lane)) continue;

                // index = beat → 배치 X좌표가 정확히 레인 끝(endpoint)이 된다
                lane.Notes.Enqueue(new NoteData(lane.beat, lane.time + duration, NoteType.HoldEnd, lane.line));
                added++;
            }

            return added;
        }

        private static bool ContinuesInNextBar(Dictionary<(int bar, int line), LaneData> byBarLine, LaneData lane)
        {
            if (!byBarLine.TryGetValue((lane.bar + 1, lane.line), out LaneData next)) return false;
            if (next.Notes.Count == 0) return false;

            NoteData first = next.Notes.Peek();
            if (first.index != 0) return false;   // 마디 첫 칸이 아니면 이어받는 게 아니다

            return first.noteType == NoteType.Holding
                || first.noteType == NoteType.HoldEnd
                || first.noteType == NoteType.HoldRelease;
        }

        private static int CountJudgeable(ChartData chartData)
        {
            int count = 0;
            foreach (LaneData lane in chartData.GetFullChartList())
                foreach (NoteData note in lane.Notes)
                    if (NoteKinds.TryFrom(note.noteType, out _)) count++;
            return count;
        }

        private static void ParseHeaderField(string line, ChartData chartData)
        {
            string content = line.TrimStart('#').Trim();
            int spaceIndex = content.IndexOf(' ');
            if (spaceIndex < 0) return;

            string key = content[..spaceIndex].ToUpper();
            string value = content[(spaceIndex + 1)..].Trim();

            switch (key)
            {
                case "NOTES":
                    if (int.TryParse(value, out int notes))
                        chartData.totalNotes = notes;
                    break;
            }
        }
    }
}