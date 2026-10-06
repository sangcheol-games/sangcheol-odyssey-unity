using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    public class BarStreamerTests
    {
        private static LaneData Lane(int bar, int line = 1) => new(bar, bar * 2.0, 4, true, line);

        private static int[] Bars(List<LaneData> lanes) => lanes.Select(l => l.bar).ToArray();

        [Test]
        public void TakeUpTo_TakesLanesBarByBar()
        {
            var streamer = new BarStreamer(new[] { Lane(0), Lane(1, 1), Lane(1, 3), Lane(2) });
            var taken = new List<LaneData>();

            streamer.TakeUpTo(0, taken);
            Assert.That(Bars(taken), Is.EqualTo(new[] { 0 }));

            taken.Clear();
            streamer.TakeUpTo(1, taken);
            Assert.That(Bars(taken), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(streamer.IsDone, Is.False);

            taken.Clear();
            streamer.TakeUpTo(2, taken);
            Assert.That(Bars(taken), Is.EqualTo(new[] { 2 }));
            Assert.That(streamer.IsDone, Is.True);
        }

        [Test]
        public void TakeUpTo_AlsoTakesEarlierBarsStillWaiting()
        {
            var streamer = new BarStreamer(new[] { Lane(1), Lane(2), Lane(4) });
            var taken = new List<LaneData>();

            streamer.TakeUpTo(2, taken);

            Assert.That(Bars(taken), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void TakeUpTo_StopsAtLaterBar()
        {
            var streamer = new BarStreamer(new[] { Lane(3) });
            var taken = new List<LaneData>();

            streamer.TakeUpTo(2, taken);

            Assert.That(taken, Is.Empty);
            Assert.That(streamer.IsDone, Is.False);
        }

        // 뷰는 채보 레인이 마디 순으로 적혀 있다고 보고 앞에서부터 꺼낸다
        [TestCase("Chart_0001_Normal", 195)]
        [TestCase("Chart_0001_Hard", 195)]
        [TestCase("Chart_0002_Easy", 155)]
        [TestCase("Chart_0002_Normal", 155)]
        [TestCase("Chart_0002_Hard", 155)]
        public void ShippedCharts_AreInBarOrder(string name, int bpm)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", name + ".txt"));
            List<LaneData> lanes = ChartParser.Parse(text, bpm).GetFullChartList();

            for (int i = 1; i < lanes.Count; i++)
                Assert.That(lanes[i].bar, Is.GreaterThanOrEqualTo(lanes[i - 1].bar), $"lane #{i}");
        }
    }
}
