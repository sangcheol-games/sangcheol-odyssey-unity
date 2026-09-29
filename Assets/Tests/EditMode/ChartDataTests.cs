using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    public class ChartDataTests
    {
        private const int Bpm = 120;

        private static ChartData Parse(params string[] lines) => ChartParser.Parse(string.Join("\n", lines), Bpm);

        [Test]
        public void BuildJudgeTrack_SortsByTimeThenLane()
        {
            ChartData chart = Parse("#001:02:1000;", "#000:04:0100;", "#000:03:1000;", "#000:01:1000;");

            JudgeNote[] track = chart.BuildJudgeTrack();

            Assert.That(track.Select(n => $"{n.Time.ToString(CultureInfo.InvariantCulture)}@{n.Lane}"),
                Is.EqualTo(new[] { "0@L1", "0@L3", "0.5@L4", "2@L2" }));
        }

        [Test]
        public void BuildJudgeTrack_AssignsNoteIdsAsTrackIndex()
        {
            ChartData chart = Parse("#001:02:1000;", "#000:04:0100;", "#000:03:1000;", "#000:01:1020;");

            JudgeNote[] track = chart.BuildJudgeTrack();

            TrackAssert.SortedByTimeThenLane(track);
            TrackAssert.IdsMatchTrack(chart, track);
        }

        [Test]
        public void BuildJudgeTrack_IsRepeatable()
        {
            ChartData chart = Parse("#000:01:0020;", "#000:12:1100;", "#001:03:1111;");

            JudgeNote[] first = chart.BuildJudgeTrack();
            int[] firstIds = chart.GetFullChartList().SelectMany(l => l.Notes).Select(n => n.id).ToArray();
            JudgeNote[] second = chart.BuildJudgeTrack();
            int[] secondIds = chart.GetFullChartList().SelectMany(l => l.Notes).Select(n => n.id).ToArray();

            Assert.That(second, Is.EqualTo(first));
            Assert.That(secondIds, Is.EqualTo(firstIds));
        }

        [Test]
        public void BuildJudgeTrack_OutOfRangeLane_ReportedAndExcluded()
        {
            ChartData chart = Parse("#000:00:1000;", "#000:05:1000;", "#000:01:1000;");
            var report = new ChartParseReport();

            JudgeNote[] track = chart.BuildJudgeTrack(report);

            Assert.That(track, Has.Length.EqualTo(1));
            Assert.That(report.TrackNotes, Is.EqualTo(1));
            Assert.That(report.Errors, Has.Count.EqualTo(2));
            Assert.That(chart.GetFullChartList().Where(l => l.line != 1).SelectMany(l => l.Notes).Select(n => n.id),
                Is.All.EqualTo(-1));
        }

        [Test]
        public void BuildJudgeTrack_SkipsBodies_MapsBothEndCharsToTail()
        {
            // 레인1: 머리 2.0, 본체 2.5, 꼬리(4) 3.0 / 레인2: 머리 2.0, 꼬리(5) 3.5
            ChartData chart = Parse("#001:01:2340;", "#001:02:2005;");

            JudgeNote[] track = chart.BuildJudgeTrack();

            Assert.That(track.Select(n => $"{n.Lane}:{n.Kind}"), Is.EqualTo(new[] { "L1:HoldHead", "L2:HoldHead", "L1:HoldTail", "L2:HoldTail" }));
            NoteData body = chart.GetFullChartList()[0].Notes.Single(n => n.index == 1);
            Assert.That(body.id, Is.EqualTo(-1), "본체는 트랙에 없다");
        }

        [Test]
        public void BuildJudgeTrack_PairsHeadWithNextTailOnSameLane()
        {
            ChartData chart = Parse("#001:01:20402040;", "#001:02:00200005;");

            JudgeNote[] track = chart.BuildJudgeTrack();

            for (int i = 0; i < track.Length; i++)
            {
                JudgeNote note = track[i];
                Assert.That(note.PairId, Is.GreaterThanOrEqualTo(0), $"#{i} {note}");
                JudgeNote pair = track[note.PairId];
                Assert.That(pair.PairId, Is.EqualTo(i), "짝은 서로를 가리킨다");
                Assert.That(pair.Lane, Is.EqualTo(note.Lane));
                if (note.Kind == NoteKind.HoldHead) Assert.That(pair.Time, Is.GreaterThan(note.Time));
            }
            // 2.0 L1머리, 2.5 L1꼬리, 2.5 L2머리, 3.0 L1머리, 3.5 L1꼬리, 3.75 L2꼬리
            Assert.That(track.Select(n => n.PairId), Is.EqualTo(new[] { 1, 0, 5, 4, 3, 2 }));
        }

        [Test]
        public void BuildJudgeTrack_UnpairedNotes_AreWarned()
        {
            // 레인1: 꼬리(5)만 / 레인2: 머리 두 개 뒤에 꼬리 하나
            ChartData chart = Parse("#001:01:0005;", "#001:02:2204;");
            var report = new ChartParseReport();

            JudgeNote[] track = chart.BuildJudgeTrack(report);

            Assert.That(report.Warnings, Has.Count.EqualTo(2));
            Assert.That(track.Count(n => n.PairId < 0), Is.EqualTo(2), "머리 없는 꼬리 1 + 꼬리 없는 머리 1");
        }
    }
}
