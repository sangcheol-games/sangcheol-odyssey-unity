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
            public int Tap, HoldHead, HoldTail, Bodies;   // HoldTail은 4 + 5 + 합성분. Bodies는 판정에서 빠지는 3

            public int Track => Tap + HoldHead + HoldTail;
            public override string ToString() => Name;
        }

        // Bpm은 MusicSO 에셋 값
        private static readonly Case[] Cases =
        {
            new() { Name = "Chart_0001_Normal", Bpm = 195, Header = 551, Synthesized = 7, Tap = 340, HoldHead = 109, HoldTail = 109, Bodies = 0 },
            new() { Name = "Chart_0001_Hard", Bpm = 195, Header = 562, Synthesized = 8, Tap = 356, HoldHead = 107, HoldTail = 107, Bodies = 0 },
            new() { Name = "Chart_0002_Easy", Bpm = 155, Header = 252, Synthesized = 17, Tap = 171, HoldHead = 49, HoldTail = 49, Bodies = 0 },
            new() { Name = "Chart_0002_Normal", Bpm = 155, Header = 353, Synthesized = 19, Tap = 266, HoldHead = 53, HoldTail = 53, Bodies = 0 },
            new() { Name = "Chart_0002_Hard", Bpm = 155, Header = 414, Synthesized = 5, Tap = 309, HoldHead = 55, HoldTail = 55, Bodies = 0 },
            new() { Name = "Chart_0003_Easy", Bpm = 160, Header = 343, Synthesized = 2, Tap = 157, HoldHead = 94, HoldTail = 94, Bodies = 0 },
            new() { Name = "Chart_0003_Normal", Bpm = 160, Header = 641, Synthesized = 2, Tap = 241, HoldHead = 201, HoldTail = 201, Bodies = 0 },
        };

        private static string KindCounts(int tap, int head, int tail, int bodies)
            => $"Tap={tap} HoldHead={head} HoldTail={tail} Bodies={bodies}";

        [TestCaseSource(nameof(Cases))]
        public void ParseAndBuild_MatchKnownCounts(Case c)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", c.Name + ".txt"));
            var report = new ChartParseReport();

            ChartData chart = ChartParser.Parse(text, c.Bpm, report);
            JudgeNote[] track = chart.BuildJudgeTrack(report);

            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Warnings, Is.Empty, "짝 없는 머리·꼬리가 없어야 한다");
            Assert.That(report.HeaderNotes, Is.EqualTo(c.Header));
            Assert.That(report.Synthesized, Is.EqualTo(c.Synthesized));
            Assert.That(chart.totalNotes, Is.EqualTo(c.Track), "총 노트 수 = 판정 트랙 길이");
            Assert.That(track, Has.Length.EqualTo(c.Track));
            Assert.That(report.TrackNotes, Is.EqualTo(c.Track));

            int Count(NoteKind kind) => track.Count(n => n.Kind == kind);
            int bodies = chart.GetFullChartList().SelectMany(l => l.Notes).Count(n => n.noteType == NoteType.Holding);
            Assert.That(
                KindCounts(Count(NoteKind.Tap), Count(NoteKind.HoldHead), Count(NoteKind.HoldTail), bodies),
                Is.EqualTo(KindCounts(c.Tap, c.HoldHead, c.HoldTail, c.Bodies)));
            Assert.That(track.Where(n => n.Kind != NoteKind.Tap).All(n => n.PairId >= 0), Is.True, "모든 머리·꼬리가 짝을 가진다");

            TrackAssert.SortedByTimeThenLane(track);
            TrackAssert.IdsMatchTrack(chart, track);
        }
    }
}
