using System.Collections.Generic;
using NUnit.Framework;
using SCOdyssey.Game.Timing.Judgement;

namespace SCOdyssey.Game.Timing.Tests
{
    public class TimingLogTests
    {
        private JudgementTimeline _timeline;
        private int _epoch;
        private TimingLog _log;

        [SetUp]
        public void SetUp()
        {
            _timeline = new JudgementTimeline(() => 3);
            _epoch = 7;
            _log = new TimingLog(_timeline, () => _epoch);
        }

        [Test]
        public void Record_FillsLatchedStepsAndEpoch()
        {
            _timeline.Latch();
            _log.Record(TimingKind.Press, 2, -4.5);

            var samples = new List<TimingSample>();
            _log.CopyTo(samples);

            Assert.AreEqual(1, samples.Count);
            Assert.AreEqual(TimingKind.Press, samples[0].Kind);
            Assert.AreEqual(2, samples[0].Grade);
            Assert.AreEqual(-4.5, samples[0].ErrorMs, 1e-12);
            Assert.AreEqual(3, samples[0].JudgmentOffsetSteps);
            Assert.AreEqual(7, samples[0].Epoch);
        }

        [Test]
        public void Summarize_FiltersByKind()
        {
            _log.Record(TimingKind.Press, 0, 10.0);
            _log.Record(TimingKind.Release, 0, 100.0);
            _log.Record(TimingKind.Press, 0, 20.0);

            TimingStats stats = _log.Summarize(TimingKind.Press, 64);

            Assert.AreEqual(2, stats.Count);
            Assert.AreEqual(15.0, stats.MeanMs, 1e-9);
            Assert.AreEqual(5.0, stats.StdDevMs, 1e-9);
        }

        [Test]
        public void Summarize_UsesMostRecentSamples()
        {
            _log.Record(TimingKind.Press, 0, 100.0);
            _log.Record(TimingKind.Press, 0, 1.0);
            _log.Record(TimingKind.Press, 0, 3.0);

            TimingStats stats = _log.Summarize(TimingKind.Press, 2);

            Assert.AreEqual(2, stats.Count);
            Assert.AreEqual(2.0, stats.MeanMs, 1e-9);
        }

        [Test]
        public void Ring_OverwritesOldest()
        {
            int extra = 10;
            for (int i = 0; i < TimingLog.Capacity + extra; i++) _log.Record(TimingKind.Press, 0, i);

            var samples = new List<TimingSample>();
            _log.CopyTo(samples);

            Assert.AreEqual(TimingLog.Capacity, _log.Count);
            Assert.AreEqual(TimingLog.Capacity + extra, _log.TotalRecorded);
            Assert.AreEqual(extra, samples[0].ErrorMs, 1e-12);
            Assert.AreEqual(TimingLog.Capacity + extra - 1, samples[samples.Count - 1].ErrorMs, 1e-12);
        }

        [Test]
        public void NaN_IsIgnored()
        {
            _log.Record(TimingKind.Miss, 0, double.NaN);

            Assert.AreEqual(0, _log.Count);
        }
    }
}
