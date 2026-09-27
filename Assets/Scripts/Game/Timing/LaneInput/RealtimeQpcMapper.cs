using System;
using SCOdyssey.Core;
using UnityEngine.InputSystem.LowLevel;

namespace SCOdyssey.Game.Timing.LaneInput
{
    // Input System 시각(ctx.time, InputState.currentTime과 같은 시간축)을 QPC 틱으로 바꾼다.
    // 프레임마다 (QPC 앞, InputState.currentTime, QPC 뒤)를 읽어 오프셋 o = QPC 중간값(초) - t 를 구하고,
    // 최근 32개의 중앙값을 쓴다. 두 QPC 차이가 50µs를 넘는 표본은 버린다(읽는 사이에 스레드가 밀림).
    internal sealed class RealtimeQpcMapper
    {
        public const int WindowSize = 32;
        public const double MaxReadSeconds = 50e-6;

        private readonly Func<double> _readInputTime;
        private readonly double[] _offsets = new double[WindowSize];
        private readonly double[] _sorted = new double[WindowSize];
        private int _next;
        private int _count;
        private double _median;

        public RealtimeQpcMapper()
            : this(ReadInputStateTime)
        {
        }

        internal RealtimeQpcMapper(Func<double> readInputTime)
        {
            _readInputTime = readInputTime;
        }

        public bool HasOffset
        {
            get { return _count > 0; }
        }

        public double OffsetSeconds
        {
            get { return _median; }
        }

        public int RejectedSamples { get; private set; }

        public void Sample()
        {
            long before = Qpc.Now;
            double inputTime = _readInputTime();
            long after = Qpc.Now;
            AddSample(before, inputTime, after);
        }

        // 받아들였으면 true.
        public bool AddSample(long qpcBefore, double inputTime, long qpcAfter)
        {
            if (Qpc.ToSeconds(qpcAfter - qpcBefore) > MaxReadSeconds)
            {
                RejectedSamples++;
                return false;
            }

            double mid = Qpc.ToSeconds(qpcBefore) + Qpc.ToSeconds(qpcAfter - qpcBefore) * 0.5;
            _offsets[_next] = mid - inputTime;
            _next = (_next + 1) % WindowSize;
            if (_count < WindowSize) _count++;
            _median = ComputeMedian();
            return true;
        }

        public long ToQpc(double inputTime)
        {
            return Qpc.FromSeconds(inputTime + _median);
        }

        public void Reset()
        {
            _next = 0;
            _count = 0;
            _median = 0;
            RejectedSamples = 0;
        }

        private static double ReadInputStateTime()
        {
            return InputState.currentTime;
        }

        private double ComputeMedian()
        {
            for (int i = 0; i < _count; i++)
            {
                double value = _offsets[i];
                int j = i - 1;
                while (j >= 0 && _sorted[j] > value)
                {
                    _sorted[j + 1] = _sorted[j];
                    j--;
                }
                _sorted[j + 1] = value;
            }

            int middle = _count / 2;
            if (_count % 2 == 1) return _sorted[middle];
            return (_sorted[middle - 1] + _sorted[middle]) * 0.5;
        }
    }
}
