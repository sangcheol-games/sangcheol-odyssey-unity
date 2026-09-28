using NUnit.Framework;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing.LaneInput;

namespace SCOdyssey.Game.Timing.Tests
{
    public class RealtimeQpcMapperTests
    {
        private const double Tolerance = 1e-6;

        private static void AddOffset(RealtimeQpcMapper mapper, double offsetSeconds)
        {
            long qpc = Qpc.FromSeconds(100.0 + offsetSeconds);
            mapper.AddSample(qpc, 100.0, qpc);
        }

        [Test]
        public void Offset_IsMedianOfSamples()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            AddOffset(mapper, 1.0);
            AddOffset(mapper, 5.0);
            AddOffset(mapper, 3.0);

            Assert.IsTrue(mapper.HasOffset);
            Assert.AreEqual(3.0, mapper.OffsetSeconds, Tolerance);
        }

        [Test]
        public void EvenCount_AveragesMiddlePair()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            AddOffset(mapper, 1.0);
            AddOffset(mapper, 2.0);

            Assert.AreEqual(1.5, mapper.OffsetSeconds, Tolerance);
        }

        [Test]
        public void SlowRead_IsRejected()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            bool accepted = mapper.AddSample(0, 0.0, Qpc.FromSeconds(0.001));

            Assert.IsFalse(accepted);
            Assert.IsFalse(mapper.HasOffset);
            Assert.AreEqual(1, mapper.RejectedSamples);
        }

        [Test]
        public void Window_KeepsOnlyRecentSamples()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            for (int i = 0; i < RealtimeQpcMapper.WindowSize; i++) AddOffset(mapper, 10.0);
            for (int i = 0; i < RealtimeQpcMapper.WindowSize / 2 + 1; i++) AddOffset(mapper, 20.0);

            Assert.AreEqual(20.0, mapper.OffsetSeconds, Tolerance);
        }

        [Test]
        public void SingleOutlier_DoesNotMoveMedian()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            for (int i = 0; i < 10; i++) AddOffset(mapper, 2.0);
            AddOffset(mapper, 2.5);

            Assert.AreEqual(2.0, mapper.OffsetSeconds, Tolerance);
        }

        [Test]
        public void ToQpc_AddsOffset()
        {
            var mapper = new RealtimeQpcMapper(() => 0.0);
            AddOffset(mapper, 2.0);

            Assert.AreEqual(7.0, Qpc.ToSeconds(mapper.ToQpc(5.0)), Tolerance);
        }

        [Test]
        public void Sample_ReadsInjectedInputTime()
        {
            var mapper = new RealtimeQpcMapper(() => Qpc.ToSeconds(Qpc.Now) - 50.0);
            mapper.Sample();

            Assert.IsTrue(mapper.HasOffset);
            Assert.AreEqual(50.0, mapper.OffsetSeconds, 0.001);
        }
    }
}
