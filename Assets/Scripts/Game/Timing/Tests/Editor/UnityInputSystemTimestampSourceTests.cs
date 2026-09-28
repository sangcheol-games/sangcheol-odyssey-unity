using System.Collections.Generic;
using NUnit.Framework;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing.LaneInput;

namespace SCOdyssey.Game.Timing.Tests
{
    public class UnityInputSystemTimestampSourceTests
    {
        private const double OffsetSeconds = 50.0;
        private static readonly long Tolerance = Qpc.FromSeconds(0.001);

        private bool _focused;
        private UnityInputSystemTimestampSource _source;
        private readonly List<LaneInputEvent> _drained = new List<LaneInputEvent>();

        [SetUp]
        public void SetUp()
        {
            _focused = true;
            // 입력 시간축 = QPC(초) - 50초.
            var mapper = new RealtimeQpcMapper(() => Qpc.ToSeconds(Qpc.Now) - OffsetSeconds);
            _source = new UnityInputSystemTimestampSource(mapper, () => _focused);
            _drained.Clear();
        }

        [Test]
        public void Push_ConvertsInputTimeToQpc()
        {
            _source.Push(2, true, 10.0);
            _source.Drain(_drained);

            Assert.AreEqual(1, _drained.Count);
            Assert.AreEqual(2, _drained[0].Lane);
            Assert.IsTrue(_drained[0].IsDown);
            Assert.IsFalse(_drained[0].IsSynthetic);
            Assert.AreEqual(Qpc.FromSeconds(10.0 + OffsetSeconds), _drained[0].QpcTicks, Tolerance);
        }

        [Test]
        public void Drain_EmptiesBuffer()
        {
            _source.Push(1, true, 1.0);
            _source.Push(1, false, 1.1);

            Assert.AreEqual(2, _source.Drain(_drained));
            Assert.AreEqual(0, _source.Drain(_drained));
            Assert.AreEqual(2, _drained.Count);
        }

        [Test]
        public void Overflow_DropsOldestFirst()
        {
            int extra = 3;
            for (int i = 0; i < UnityInputSystemTimestampSource.Capacity + extra; i++) _source.Push(1, true, i);

            _source.Drain(_drained);

            Assert.AreEqual(UnityInputSystemTimestampSource.Capacity, _drained.Count);
            Assert.AreEqual(extra, _source.DroppedCount);
            Assert.AreEqual(Qpc.FromSeconds(extra + OffsetSeconds), _drained[0].QpcTicks, Tolerance);
            Assert.AreEqual(Qpc.FromSeconds(UnityInputSystemTimestampSource.Capacity + extra - 1 + OffsetSeconds), _drained[_drained.Count - 1].QpcTicks, Tolerance);
        }

        [Test]
        public void OrderIsKept_AfterWrapAround()
        {
            for (int i = 0; i < 200; i++) _source.Push(1, true, i);
            _source.Drain(_drained);
            _drained.Clear();

            for (int i = 0; i < 100; i++) _source.Push(1, true, 1000 + i);
            _source.Drain(_drained);

            Assert.AreEqual(100, _drained.Count);
            for (int i = 1; i < _drained.Count; i++) Assert.Greater(_drained[i].QpcTicks, _drained[i - 1].QpcTicks);
        }

        [Test]
        public void SyntheticScope_MarksEvents()
        {
            _source.BeginSynthetic();
            _source.BeginSynthetic();
            _source.Push(1, false, 1.0);
            _source.EndSynthetic();
            _source.Push(2, false, 1.1);
            _source.EndSynthetic();
            _source.Push(3, false, 1.2);

            _source.Drain(_drained);

            Assert.IsTrue(_drained[0].IsSynthetic);
            Assert.IsTrue(_drained[1].IsSynthetic);
            Assert.IsFalse(_drained[2].IsSynthetic);
            Assert.AreEqual(2, _source.SyntheticCount);
        }

        [Test]
        public void EndWithoutBegin_IsIgnored()
        {
            _source.EndSynthetic();
            _source.BeginSynthetic();
            _source.Push(1, false, 1.0);

            _source.Drain(_drained);

            Assert.IsTrue(_drained[0].IsSynthetic);
        }

        [Test]
        public void UnfocusedPush_IsSynthetic()
        {
            _focused = false;
            _source.Push(1, false, 1.0);
            _focused = true;
            _source.Push(1, true, 2.0);

            _source.Drain(_drained);

            Assert.IsTrue(_drained[0].IsSynthetic);
            Assert.IsFalse(_drained[1].IsSynthetic);
        }
    }
}
