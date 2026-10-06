using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    public class BarClockTests
    {
        [TestCase(195)]
        [TestCase(155)]
        [TestCase(120)]
        public void FromBpm_UsesFloatBarLength(int bpm)
        {
            BarClock clock = BarClock.FromBpm(bpm);

            // 파서가 노트 시각을 만들던 float 식 그대로여야 마디 경계와 노트 시각이 비트 단위로 맞는다
            Assert.That(clock.BarDuration, Is.EqualTo((double)((60f / bpm) * 4f)));
            Assert.That(clock.BeatDuration, Is.EqualTo((double)((60f / bpm) * 4f) / 4.0));
        }

        [Test]
        public void BarStart_IsBarTimesDuration()
        {
            var clock = new BarClock(2.0);

            Assert.That(clock.BarStart(0), Is.EqualTo(0));
            Assert.That(clock.BarStart(3), Is.EqualTo(6.0));
            Assert.That(clock.BeatDuration, Is.EqualTo(0.5));
        }

        [TestCase("Chart_0001_Hard", 195)]
        [TestCase("Chart_0002_Easy", 155)]
        public void BarStart_MatchesParsedLaneTimes(string name, int bpm)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", name + ".txt"));
            ChartData chart = ChartParser.Parse(text, bpm);
            BarClock clock = BarClock.FromBpm(bpm);

            foreach (LaneData lane in chart.GetFullChartList())
                Assert.That(lane.time, Is.EqualTo(clock.BarStart(lane.bar)), $"bar {lane.bar} line {lane.line}");
        }
    }
}
