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
    }
}
