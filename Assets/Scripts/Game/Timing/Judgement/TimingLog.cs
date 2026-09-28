using System;
using System.Collections.Generic;

namespace SCOdyssey.Game.Timing.Judgement
{
    public readonly struct TimingStats
    {
        public readonly int Count;
        public readonly double MeanMs;
        public readonly double StdDevMs;

        public TimingStats(int count, double meanMs, double stdDevMs)
        {
            Count = count;
            MeanMs = meanMs;
            StdDevMs = stdDevMs;
        }
    }

    // 판정 오차 기록(최근 4096건 링). 게임플레이가 판정을 확정할 때 Record를 부르고,
    // 판정 싱크 단계(래치 값)와 Epoch는 로그가 채운다. 개발 오버레이와 후속 캘리브레이션이 읽는다.
    public sealed class TimingLog : IJudgementTimingLog
    {
        public const int Capacity = 4096;

        private readonly TimingSample[] _ring = new TimingSample[Capacity];
        private readonly JudgementTimeline _timeline;
        private readonly Func<int> _readEpoch;
        private int _next;
        private int _count;

        internal TimingLog(JudgementTimeline timeline, Func<int> readEpoch)
        {
            _timeline = timeline;
            _readEpoch = readEpoch;
        }

        public int Count
        {
            get { return _count; }
        }

        public int TotalRecorded { get; private set; }

        public void Record(TimingKind kind, int grade, double errorMs)
        {
            if (double.IsNaN(errorMs)) return;
            int epoch = 0;
            if (_readEpoch != null) epoch = _readEpoch();
            _ring[_next] = new TimingSample(kind, grade, errorMs, _timeline.Steps, epoch);
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            TotalRecorded++;
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
        }

        // 오래된 것부터 into 뒤에 붙인다.
        public void CopyTo(List<TimingSample> into)
        {
            int start = (_next - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++)
            {
                into.Add(_ring[(start + i) % Capacity]);
            }
        }

        // 최근 maxSamples건 안에서 해당 종류의 평균과 표준편차. 할당하지 않는다.
        public TimingStats Summarize(TimingKind kind, int maxSamples)
        {
            int scanned = 0;
            int count = 0;
            double sum = 0;
            double sumSquares = 0;
            int index = _next;
            while (scanned < _count && count < maxSamples)
            {
                index = (index - 1 + Capacity) % Capacity;
                scanned++;
                TimingSample sample = _ring[index];
                if (sample.Kind != kind) continue;
                count++;
                sum += sample.ErrorMs;
                sumSquares += sample.ErrorMs * sample.ErrorMs;
            }

            if (count == 0) return new TimingStats(0, 0, 0);
            double mean = sum / count;
            double variance = sumSquares / count - mean * mean;
            if (variance < 0) variance = 0;
            return new TimingStats(count, mean, Math.Sqrt(variance));
        }
    }
}
