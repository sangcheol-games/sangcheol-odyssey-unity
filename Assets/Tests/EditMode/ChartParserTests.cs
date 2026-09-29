using System.Globalization;
using System.Linq;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class ChartParserTests
    {
        // 마디 2초, 4비트면 한 칸 0.5초. float로 계산해도 정확히 떨어진다
        private const int Bpm = 120;

        private static ChartData Parse(out ChartParseReport report, params string[] lines)
        {
            report = new ChartParseReport();
            return ChartParser.Parse(string.Join("\n", lines), Bpm, report);
        }

        private static ChartData Parse(params string[] lines) => Parse(out _, lines);

        private static LaneData LaneAt(ChartData chart, int bar, int line)
            => chart.GetFullChartList().Single(l => l.bar == bar && l.line == line);

        // "비트인덱스:종류@시각" 나열
        private static string Describe(LaneData lane)
            => string.Join(" ", lane.Notes.Select(n => $"{n.index}:{n.noteType}@{n.time.ToString("0.###", CultureInfo.InvariantCulture)}"));

        [TestCase("#001:01:1000;", true, "0:Normal@2")]
        [TestCase("#001:11:1000;", false, "3:Normal@3.5")]
        [TestCase("#001:01:10000100;", true, "0:Normal@2 5:Normal@3.25")]
        [TestCase("#001:11:10000100;", false, "2:Normal@2.5 7:Normal@3.75")]
        public void NoteIndexAndTime_RtlReversesSequence(string line, bool isLTR, string expected)
        {
            LaneData lane = LaneAt(Parse(line), 1, 1);

            Assert.That(lane.isLTR, Is.EqualTo(isLTR));
            Assert.That(lane.time, Is.EqualTo(2.0));
            Assert.That(Describe(lane), Is.EqualTo(expected));
        }

        [Test]
        public void Digits_MapToNoteTypes_OthersIgnored()
        {
            ChartData chart = Parse("#000:01:0123456789;");
            LaneData lane = LaneAt(chart, 0, 1);

            Assert.That(lane.beat, Is.EqualTo(10));
            Assert.That(chart.totalNotes, Is.EqualTo(4), "본체(3)는 세지 않는다");
            Assert.That(lane.Notes.Select(n => n.noteType), Is.EqualTo(new[]
            {
                NoteType.Normal, NoteType.HoldStart, NoteType.Holding, NoteType.HoldEnd, NoteType.HoldRelease,
            }));
        }

        [TestCase("#000:01:20045000;", 3, false)]   // 첫 4/5까지
        [TestCase("#000:11:00054002;", 3, false)]   // 반전 후 순서로 찾는다
        [TestCase("#000:01:02003000;", 7, true)]    // 종료 문자가 없으면 마디 끝까지
        [TestCase("#000:01:20002005;", 7, false)]   // 사이의 다른 머리는 건너뛰고 첫 4/5까지
        public void HoldBarBeats_ReachFirstEndCharOrBarEnd(string line, int beats, bool runsToBarEnd)
        {
            LaneData lane = LaneAt(Parse(line), 0, 1);
            NoteData head = lane.Notes.First(n => n.noteType == NoteType.HoldStart);

            Assert.That(head.holdBarBeats, Is.EqualTo(beats));
            Assert.That(lane.holdRunsToBarEnd, Is.EqualTo(runsToBarEnd));
        }

        [Test]
        public void BarEndSynth_AddsHoldEndAtBarEnd()
        {
            ChartData chart = Parse(out ChartParseReport report, "#NOTES 2", "#000:01:0020;");

            Assert.That(Describe(LaneAt(chart, 0, 1)), Is.EqualTo("2:HoldStart@1 4:HoldEnd@2"));
            Assert.That(report.Synthesized, Is.EqualTo(1));
            Assert.That(report.HeaderNotes, Is.EqualTo(2));
            Assert.That(chart.totalNotes, Is.EqualTo(2), "머리 + 합성 꼬리");
        }

        [TestCase("3000")]
        [TestCase("4000")]
        [TestCase("5000")]
        public void BarEndSynth_SkippedWhenNextBarContinuesAtFirstBeat(string next)
        {
            ChartData chart = Parse(out ChartParseReport report, "#000:01:0020;", $"#001:01:{next};");

            Assert.That(report.Synthesized, Is.Zero);
            Assert.That(Describe(LaneAt(chart, 0, 1)), Is.EqualTo("2:HoldStart@1"));
        }

        [TestCase("1000")]   // 다른 종류로 시작
        [TestCase("2000")]
        [TestCase("0300")]   // 첫 칸이 아님
        [TestCase("0000")]   // 노트 없음
        public void BarEndSynth_KeptWhenNextBarDoesNotContinue(string next)
        {
            ChartData chart = Parse("#000:01:0020;", $"#001:01:{next};");

            Assert.That(Describe(LaneAt(chart, 0, 1)), Is.EqualTo("2:HoldStart@1 4:HoldEnd@2"));
        }

        [TestCase("0003", 0)]   // 반전하면 3000 → 이어짐
        [TestCase("3000", 1)]   // 반전하면 0003 → 첫 칸이 아님
        public void BarEndSynth_ContinuationCheckedAfterRtlReversal(string nextRtl, int synthesized)
        {
            Parse(out ChartParseReport report, "#000:01:0020;", $"#001:11:{nextRtl};");

            Assert.That(report.Synthesized, Is.EqualTo(synthesized));
        }

        [Test]
        public void BarEndSynth_NextBarOnOtherLaneDoesNotContinue()
        {
            Parse(out ChartParseReport report, "#000:01:0020;", "#001:02:3000;");

            Assert.That(report.Synthesized, Is.EqualTo(1));
        }

        [Test]
        public void Header_NotesIsOnlyReported_TotalCountsJudgeableNotes()
        {
            ChartData chart = Parse(out ChartParseReport report,
                "#TITLE Some Song", "#BPM 999", "#NOTES 42", "#NOTES", "not a chart line", "#001:01:1000;");

            Assert.That(chart.bpm, Is.EqualTo(Bpm));
            Assert.That(report.HeaderNotes, Is.EqualTo(42));
            Assert.That(chart.totalNotes, Is.EqualTo(1));
            Assert.That(chart.BuildJudgeTrack(), Has.Length.EqualTo(1));
            Assert.That(report.Errors, Is.Empty);
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void LineEndings_AllAccepted(string newline)
        {
            ChartData chart = ChartParser.Parse(string.Join(newline, "#NOTES 2", "#000:01:1000;", "#001:02:1000;"), Bpm);

            Assert.That(chart.totalNotes, Is.EqualTo(2));
            Assert.That(chart.GetFullChartList(), Has.Count.EqualTo(2));
        }

        [TestCase("#0x1:01:1000;")]   // 마디 번호가 숫자가 아님
        [TestCase("#001:0:1000;")]    // 채널/레인 자리가 한 글자
        public void MalformedDataLine_ReportedAndSkipped(string bad)
        {
            ChartData chart = Parse(out ChartParseReport report, bad, "#002:01:1000;");

            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(chart.GetFullChartList().Select(l => l.bar), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void BarDuration_IsComputedInSinglePrecision()
        {
            int bpm = 155;   // const면 컴파일러가 상수로 접어버려 파서와 다른 경로가 된다
            double floatBar = (60f / bpm) * 4f;

            NoteData note = ChartParser.Parse("#003:01:1000;", bpm).GetFullChartList()[0].Notes.Peek();

            Assert.That(note.time, Is.EqualTo(3 * floatBar));
            Assert.That(note.time, Is.Not.EqualTo(3 * 240.0 / bpm));
        }
    }
}
