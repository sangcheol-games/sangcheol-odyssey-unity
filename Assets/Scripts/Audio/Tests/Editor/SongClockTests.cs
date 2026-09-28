using System.Collections.Generic;
using NUnit.Framework;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Core;

namespace SCOdyssey.Audio.Tests
{
    public class SongClockTests
    {
        private const int Rate = 48000;

        private DspQpcModel _model;
        private SongClock _clock;

        [SetUp]
        public void SetUp()
        {
            _model = new DspQpcModel(Rate, 256, 4);
            _clock = new SongClock(_model);
        }

        private static long Ticks(double seconds)
        {
            return Qpc.FromSeconds(seconds);
        }

        // 믹서가 계단 없이 t·R로 흐르는 이상적인 샘플을 from~to 동안 넣는다.
        private void FeedSteady(double from, double to)
        {
            for (double t = from; t <= to; t += 0.005)
            {
                long q = Ticks(t);
                _model.AddSample(q, (ulong)(t * Rate), q);
            }
        }

        [Test]
        public void BeforeAnySegment_FrameIsZeroAndNotRunning()
        {
            _clock.UpdateFrame(Ticks(1.0));

            Assert.AreEqual(0.0, _clock.Frame.SongTime);
            Assert.IsFalse(_clock.Frame.IsRunning);
            SongTimePoint point;
            Assert.IsFalse(_clock.TrySongTimeAt(Ticks(1.0), out point));
        }

        [Test]
        public void RunningSegment_HoldsTau0UntilStartDspThenAdvances()
        {
            FeedSteady(0, 1.0);
            // 1.0초(DSP 48000)에 커밋, Cf = 1.02초에 곡 시각 5.0부터 흐른다.
            _clock.CommitRunning(5.0, 1.02 * Rate, Ticks(1.0), DiscontinuityReason.Start);

            SongTimePoint guard;
            Assert.IsTrue(_clock.TrySongTimeAt(Ticks(1.01), out guard));
            Assert.AreEqual(5.0, guard.SongTime, 1e-9);
            Assert.IsTrue(guard.IsRunning);

            FeedSteady(1.0, 1.52);
            SongTimePoint later;
            _clock.TrySongTimeAt(Ticks(1.52), out later);
            Assert.AreEqual(5.5, later.SongTime, 1e-3);
        }

        [Test]
        public void Freeze_FixesSongTimeAndRaisesDiscontinuity()
        {
            var events = new List<ClockDiscontinuity>();
            _clock.Discontinuity += events.Add;
            FeedSteady(0, 1.0);
            _clock.CommitRunning(0, 1.0 * Rate, Ticks(1.0), DiscontinuityReason.Start);
            FeedSteady(1.0, 2.0);

            _clock.Freeze(1.0, Ticks(2.0), DiscontinuityReason.Pause);
            FeedSteady(2.0, 3.0);
            _clock.UpdateFrame(Ticks(3.0));

            Assert.AreEqual(1.0, _clock.Frame.SongTime, 1e-9);
            Assert.IsFalse(_clock.Frame.IsRunning);
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(DiscontinuityReason.Pause, events[1].Reason);
            Assert.AreEqual(1, events[1].PreviousEpoch);
            Assert.AreEqual(2, events[1].Epoch);
        }

        [Test]
        public void InputBeforePause_UsesPreviousRunningSegment()
        {
            FeedSteady(0, 1.0);
            _clock.CommitRunning(0, 1.0 * Rate, Ticks(1.0), DiscontinuityReason.Start);
            FeedSteady(1.0, 2.0);
            _clock.Freeze(0.99, Ticks(1.995), DiscontinuityReason.Pause);

            SongTimePoint beforePause;
            _clock.TrySongTimeAt(Ticks(1.5), out beforePause);
            Assert.IsTrue(beforePause.IsRunning);
            Assert.AreEqual(1, beforePause.Epoch);
            Assert.AreEqual(0.5, beforePause.SongTime, 1e-3);

            SongTimePoint afterPause;
            _clock.TrySongTimeAt(Ticks(2.0), out afterPause);
            Assert.IsFalse(afterPause.IsRunning);
            Assert.AreEqual(2, afterPause.Epoch);
        }

        [Test]
        public void SegmentsOlderThanHistory_AreNotFound()
        {
            for (int i = 0; i <= SongTimeline.History; i++)
            {
                _clock.Freeze(i, Ticks(i + 1), DiscontinuityReason.Pause);
            }

            SongTimePoint point;
            Assert.IsFalse(_clock.TrySongTimeAt(Ticks(1.5), out point));
            Assert.IsTrue(_clock.TrySongTimeAt(Ticks(2.5), out point));
        }

        [Test]
        public void Frame_DoesNotGoBackwardWithinEpoch()
        {
            FeedSteady(0, 1.0);
            _clock.CommitRunning(0, 1.0 * Rate, Ticks(1.0), DiscontinuityReason.Start);
            FeedSteady(1.0, 1.3);
            // 크게 앞선 관측(이상치)이 창에 있으면 추정이 S_max(32ms)까지 앞섰다가, 창에서 빠지면 내려간다.
            AddExact(1.305, 3000);
            FeedSteady(1.31, 2.29);
            AddExact(2.300, 0);
            _clock.UpdateFrame(Ticks(2.300));
            double before = _clock.Frame.SongTime;

            AddExact(2.310, 0);     // 1.305의 이상치가 1초 창에서 빠진다
            SongTimePoint raw;
            _clock.TrySongTimeAt(Ticks(2.310), out raw);
            Assert.Less(raw.SongTime, before, "이 테스트는 추정값이 실제로 내려가는 경우여야 한다");

            _clock.UpdateFrame(Ticks(2.310));
            Assert.AreEqual(before, _clock.Frame.SongTime);
        }

        private void AddExact(double t, int extraSamples)
        {
            long q = Ticks(t);
            _model.AddSample(q, (ulong)(t * Rate) + (ulong)extraSamples, q);
        }

        [Test]
        public void ResetModel_FreezesRunningSegmentAndInvalidatesIt()
        {
            FeedSteady(0, 1.0);
            _clock.CommitRunning(0, 1.0 * Rate, Ticks(1.0), DiscontinuityReason.Start);
            FeedSteady(1.0, 2.0);

            _clock.ResetModel(44100, 512, 4, Ticks(2.0));

            SongTimePoint frozen;
            Assert.IsTrue(_clock.TrySongTimeAt(Ticks(2.5), out frozen));
            Assert.IsFalse(frozen.IsRunning);
            Assert.AreEqual(1.0, frozen.SongTime, 1e-3);

            SongTimePoint oldRunning;
            Assert.IsFalse(_clock.TrySongTimeAt(Ticks(1.5), out oldRunning));
        }
    }
}
