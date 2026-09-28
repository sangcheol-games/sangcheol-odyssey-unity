using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    // 출시 채보가 지금의 파서/트랙에서 만드는 결과. 채보 파일이나 파싱 규칙이 바뀌면 여기가 먼저 깨진다.
    public class RealChartTests
    {
        public sealed class Case
        {
            public string Name;
            public int Bpm;
            public int Header;
            public int Synthesized;
            public int Normal, HoldStart, Holding, HoldEnd, HoldRelease;   // HoldEnd는 합성분 포함

            public int Track => Normal + HoldStart + Holding + HoldEnd + HoldRelease;
            public override string ToString() => Name;
        }

        // Bpm은 MusicSO 에셋 값
        private static readonly Case[] Cases =
        {
            new() { Name = "Chart_0001_Normal", Bpm = 195, Header = 619, Synthesized = 7, Normal = 340, HoldStart = 109, Holding = 68, HoldEnd = 93, HoldRelease = 16 },
            new() { Name = "Chart_0001_Hard", Bpm = 195, Header = 629, Synthesized = 8, Normal = 356, HoldStart = 107, Holding = 67, HoldEnd = 91, HoldRelease = 16 },
            new() { Name = "Chart_0002_Easy", Bpm = 155, Header = 252, Synthesized = 17, Normal = 171, HoldStart = 49, Holding = 0, HoldEnd = 49, HoldRelease = 0 },
            new() { Name = "Chart_0002_Normal", Bpm = 155, Header = 353, Synthesized = 19, Normal = 266, HoldStart = 53, Holding = 0, HoldEnd = 48, HoldRelease = 5 },
            new() { Name = "Chart_0002_Hard", Bpm = 155, Header = 403, Synthesized = 21, Normal = 314, HoldStart = 55, Holding = 0, HoldEnd = 48, HoldRelease = 7 },
        };

        private static string KindCounts(int normal, int holdStart, int holding, int holdEnd, int holdRelease)
            => $"Normal={normal} HoldStart={holdStart} Holding={holding} HoldEnd={holdEnd} HoldRelease={holdRelease}";

        [TestCaseSource(nameof(Cases))]
        public void ParseAndBuild_MatchKnownCounts(Case c)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", c.Name + ".txt"));
            var report = new ChartParseReport();

            ChartData chart = ChartParser.Parse(text, c.Bpm, report);
            JudgeNote[] track = chart.BuildJudgeTrack(report);

            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.HeaderNotes, Is.EqualTo(c.Header));
            Assert.That(report.Synthesized, Is.EqualTo(c.Synthesized));
            Assert.That(chart.totalNotes, Is.EqualTo(c.Header + c.Synthesized));
            Assert.That(track, Has.Length.EqualTo(c.Track));
            Assert.That(report.TrackNotes, Is.EqualTo(c.Track));

            int Count(NoteType kind) => track.Count(n => n.Kind == kind);
            Assert.That(
                KindCounts(Count(NoteType.Normal), Count(NoteType.HoldStart), Count(NoteType.Holding), Count(NoteType.HoldEnd), Count(NoteType.HoldRelease)),
                Is.EqualTo(KindCounts(c.Normal, c.HoldStart, c.Holding, c.HoldEnd, c.HoldRelease)));

            TrackAssert.SortedByTimeThenLane(track);
            TrackAssert.IdsMatchTrack(chart, track);
        }
    }
}
