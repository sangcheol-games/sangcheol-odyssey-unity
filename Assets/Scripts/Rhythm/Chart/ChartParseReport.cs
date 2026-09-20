using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 파싱/트랙 생성 중 생긴 진단. 로그 출력은 호출자(Unity 쪽)가 한다.
    public sealed class ChartParseReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public int HeaderNotes;     // #NOTES 헤더 값
        public int Synthesized;     // 마디 끝 홀드 종료 합성 개수
        public int TrackNotes;      // BuildJudgeTrack이 만든 판정 트랙 길이

        public string Summary(ChartData chart)
            => $"Chart Parsed Successfully. Total Lanes: {chart.GetFullChartList().Count}, Total Notes: {chart.totalNotes} (마디 끝 홀드 종료 {Synthesized}개 합성)";
    }
}
