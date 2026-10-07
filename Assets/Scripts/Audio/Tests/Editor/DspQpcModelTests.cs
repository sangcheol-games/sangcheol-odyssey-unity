using System;
using NUnit.Framework;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Core;

namespace SCOdyssey.Audio.Tests
{
    public class DspQpcModelTests
    {
        private const int Rate = 48000;
        private const double FrameSeconds = 1.0 / 120.0;   // Update·LateUpdate 두 번 읽는 60fps

        // 믹서는 OS 주기마다 step 샘플씩 몰아 믹스한다. 참 DSP = t·R·(1 + drift).
        private static ulong RawDsp(double t, int step, double driftPpm)
        {
            double trueDsp = t * Rate * (1.0 + driftPpm * 1e-6);
            return (ulong)(Math.Floor(trueDsp / step) * step);
        }

        private static double TrueDsp(double t, double driftPpm)
        {
            return t * Rate * (1.0 + driftPpm * 1e-6);
        }

        private static long Ticks(double seconds)
        {
            return Qpc.FromSeconds(seconds);
        }

        private static void Feed(DspQpcModel model, double t, ulong dsp)
        {
            long q = Ticks(t);
            model.AddSample(q, dsp, q);
        }

        [Test]
        public void MaxLead_IsBlockLeadOrThirtyTwoMs()
        {
            Assert.AreEqual(1536.0, new DspQpcModel(48000, 256, 4).MaxLeadSamples, 1e-9);      // 1280 < 32ms
            Assert.AreEqual(1411.2, new DspQpcModel(44100, 256, 2).MaxLeadSamples, 1e-9);      // 768 < 32ms
            Assert.AreEqual(5120.0, new DspQpcModel(48000, 1024, 4).MaxLeadSamples, 1e-9);
        }

        [Test]
        public void BurstyMixer_EstimateTracksTrueClockWithinOneMs()
        {
            var model = new DspQpcModel(Rate, 256, 4);
            var random = new Random(7);
            double t = 0;
            double worstMs = 0;
            while (t < 5.0)
            {
                Feed(model, t, RawDsp(t, 512, 0));
                if (t > 1.0)
                {
                    double estimate;
                    Assert.IsTrue(model.TryDspAt(Ticks(t), out estimate));
                    double errorMs = Math.Abs(estimate - TrueDsp(t, 0)) / Rate * 1000.0;
                    if (errorMs > worstMs) worstMs = errorMs;
                }
                t += FrameSeconds + (random.NextDouble() - 0.5) * 0.002;
            }
            Assert.Less(worstMs, 1.0);
        }

        [TestCase(50.0)]
        [TestCase(-50.0)]
        public void Drift_IsAbsorbedByRollingWindow(double driftPpm)
        {
            var model = new DspQpcModel(Rate, 256, 4);
            var random = new Random(11);
            double t = 0;
            double worstMs = 0;
            while (t < 60.0)
            {
                Feed(model, t, RawDsp(t, 480, driftPpm));
                if (t > 10.0)
                {
                    double estimate;
                    model.TryDspAt(Ticks(t), out estimate);
                    double errorMs = Math.Abs(estimate - TrueDsp(t, driftPpm)) / Rate * 1000.0;
                    if (errorMs > worstMs) worstMs = errorMs;
                }
                t += FrameSeconds + (random.NextDouble() - 0.5) * 0.002;
            }
            Assert.Less(worstMs, 1.0);
        }

        [Test]
        public void DownwardStep_IsFollowedWithinOneSecond()
        {
            // 20초 지점에서 DSP가 20ms 영구히 뒤처진다(언더런으로 믹스하지 못한 구간).
            const double lagSeconds = 0.020;
            var model = new DspQpcModel(Rate, 256, 4);
            var random = new Random(3);
            double t = 0;
            while (t < 21.2)
            {
                double shifted = t;
                if (t >= 20.0) shifted = t - lagSeconds;
                Feed(model, t, RawDsp(shifted, 512, 0));
                t += FrameSeconds + (random.NextDouble() - 0.5) * 0.002;
            }

            double estimate;
            model.TryDspAt(Ticks(t), out estimate);
            double errorMs = Math.Abs(estimate - TrueDsp(t - lagSeconds, 0)) / Rate * 1000.0;
            Assert.Less(errorMs, 1.0);
        }

        [Test]
        public void MixerStall_StopsAtLastRawPlusMaxLead()
        {
            var model = new DspQpcModel(Rate, 256, 4);
            double t = 0;
            while (t < 2.0)
            {
                Feed(model, t, RawDsp(t, 512, 0));
                t += FrameSeconds;
            }
            ulong stalled = model.LastDsp;
            double stallStart = t;
            while (t < stallStart + 0.2)
            {
                Feed(model, t, stalled);
                t += FrameSeconds;
            }

            double estimate;
            model.TryDspAt(Ticks(t), out estimate);
            Assert.AreEqual(stalled + model.MaxLeadSamples, estimate, 1e-6);
        }

        [Test]
        public void QueryBeforeLastSample_IsNotRaisedToLastRaw()
        {
            var model = new DspQpcModel(Rate, 256, 4);
            double t = 0;
            while (t < 2.0)
            {
                Feed(model, t, RawDsp(t, 512, 0));
                t += FrameSeconds;
            }
            double last = t - FrameSeconds;

            double early;
            model.TryDspAt(Ticks(last - 0.020), out early);
            Assert.Less(early, (double)model.LastDsp);

            double afterLast;
            model.TryDspAt(Ticks(last + 0.001), out afterLast);
            Assert.GreaterOrEqual(afterLast, (double)model.LastDsp);
        }

        [Test]
        public void SlowBracket_IsRejected()
        {
            var model = new DspQpcModel(Rate, 256, 4);
            long q = Ticks(1.0);
            Assert.IsFalse(model.AddSample(q, 1000, q + Ticks(100e-6)));
            Assert.AreEqual(1, model.RejectedSamples);
            Assert.IsFalse(model.HasSamples);

            double estimate;
            Assert.IsFalse(model.TryDspAt(q, out estimate));
        }

        [Test]
        public void Reset_ClearsSamplesAndBumpsVersion()
        {
            var model = new DspQpcModel(Rate, 256, 4);
            Feed(model, 1.0, 48000);
            int version = model.Version;

            model.Reset(44100, 512, 4);

            Assert.IsFalse(model.HasSamples);
            Assert.AreEqual(version + 1, model.Version);
            Assert.AreEqual(44100, model.SampleRate);
        }
    }
}
