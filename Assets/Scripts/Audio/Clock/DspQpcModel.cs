using SCOdyssey.Core;

namespace SCOdyssey.Audio.Clock
{
    // DSP 클록과 QPC의 대응. 매 프레임 (QPC 앞, DSP, QPC 뒤)를 받아 임의 QPC 시각의 DSP 값을 추정한다.
    //
    // 믹서는 OS 믹스 주기마다 여러 블록을 몰아 믹스하므로 원시 DSP는 계단진다. 관측은 항상 클록이 오른 뒤에
    // 일어나므로 offset = dsp - q·R 의 최댓값(하한 포락선)이 참 관계에 가장 가깝다. 최근 10초 창에서 그 최댓값을 쓴다.
    // 창이 짧으면 프레임 위상이 몇 개로 묶여 추정이 최대 3~4ms 오르내린다(SP3 실측). 10초면 0.3~2ms로 줄어든다.
    // 드리프트 항은 두지 않는다(창 안 드리프트 오차가 20ppm에서 0.2ms).
    //
    // 하향 계단: 1초보다 오래된 최댓값이 최근 1초 최댓값보다 8ms 넘게 높으면 DSP가 영구히 뒤처진 것(언더런 등)으로
    //            보고 버린다. 그래서 계단은 1초 안에 따라간다. 정상 측정에서 두 값의 차이는 최대 4.7ms였다.
    // 클램프: 추정값은 마지막 원시 값 + S_max를 넘지 않는다(믹서가 멈추면 곧바로 멈춘다).
    //         마지막 관측 이후 시각의 추정값은 마지막 원시 값보다 작을 수 없다.
    internal sealed class DspQpcModel
    {
        public const double MaxBracketSeconds = 50e-6;
        public const double WindowSeconds = 10.0;
        public const double RecentWindowSeconds = 1.0;
        public const double StepToleranceSeconds = 0.008;
        public const double MinMaxLeadSeconds = 0.032;

        // 덱 길이의 상한. 덱에는 offset이 내림차순인 샘플만 남으므로 보통 몇 개뿐이다.
        // 믹서가 멈추면 샘플마다 늘지만 하향 계단 규칙이 1초 넘은 것을 버린다.
        private const int Capacity = 4096;

        private static readonly long MaxBracketTicks = Qpc.FromSeconds(MaxBracketSeconds);
        private static readonly long WindowTicks = Qpc.FromSeconds(WindowSeconds);
        private static readonly long RecentWindowTicks = Qpc.FromSeconds(RecentWindowSeconds);

        // 하한 포락선용 단조 덱(offset 내림차순). 맨 앞이 창 안 최댓값이다.
        private readonly long[] _windowQpc = new long[Capacity];
        private readonly double[] _windowOffset = new double[Capacity];
        private int _head;
        private int _count;

        private int _sampleRate;
        private double _maxLeadSamples;
        private double _stepToleranceSamples;
        private bool _hasOrigin;
        private long _originQpc;
        private ulong _originDsp;
        private long _lastQpc;
        private ulong _lastDsp;

        public DspQpcModel(int sampleRate, int blockLength, int blockCount)
        {
            Reset(sampleRate, blockLength, blockCount);
        }

        public int SampleRate
        {
            get { return _sampleRate; }
        }

        public double MaxLeadSamples
        {
            get { return _maxLeadSamples; }
        }

        public bool HasSamples
        {
            get { return _count > 0; }
        }

        public ulong LastDsp
        {
            get { return _lastDsp; }
        }

        public int RejectedSamples { get; private set; }

        // Reset마다 오른다. 이전 버전으로 만든 세그먼트는 DSP 도메인이 달라 쓰지 않는다.
        public int Version { get; private set; }

        // 세대 변경, setDriver, DEVICEREINITIALIZE에서 부른다.
        public void Reset(int sampleRate, int blockLength, int blockCount)
        {
            _sampleRate = sampleRate;
            // S_max = max(L·(N+1), 32ms). 프레임 사이 계단으로 갱신하지 않는다(메인 스레드 멈춤이 섞인다).
            double blockLead = (double)blockLength * (blockCount + 1);
            double floor = MinMaxLeadSeconds * sampleRate;
            _maxLeadSamples = blockLead;
            if (floor > blockLead) _maxLeadSamples = floor;
            _stepToleranceSamples = StepToleranceSeconds * sampleRate;

            _head = 0;
            _count = 0;
            _hasOrigin = false;
            _lastQpc = 0;
            _lastDsp = 0;
            RejectedSamples = 0;
            Version++;
        }

        // 두 QPC 사이가 50µs를 넘으면 읽는 도중 스레드가 밀린 것이므로 버린다.
        public bool AddSample(long qpcBefore, ulong dspClock, long qpcAfter)
        {
            if (qpcAfter - qpcBefore > MaxBracketTicks)
            {
                RejectedSamples++;
                return false;
            }

            long mid = qpcBefore + (qpcAfter - qpcBefore) / 2;
            if (!_hasOrigin)
            {
                _hasOrigin = true;
                _originQpc = mid;
                _originDsp = dspClock;
            }

            double offset = DspSinceOrigin(dspClock) - QpcToSamples(mid - _originQpc);

            while (_count > 0 && _windowOffset[Slot(_count - 1)] <= offset) _count--;
            if (_count == Capacity) DropFront();
            int slot = Slot(_count);
            _windowQpc[slot] = mid;
            _windowOffset[slot] = offset;
            _count++;

            while (_count > 1 && _windowQpc[_head] < mid - WindowTicks) DropFront();
            DropStaleAfterStep(mid - RecentWindowTicks);

            _lastQpc = mid;
            _lastDsp = dspClock;
            return true;
        }

        public bool TryDspAt(long qpcTicks, out double dspClock)
        {
            if (_count == 0)
            {
                dspClock = 0;
                return false;
            }

            double estimate = _originDsp + QpcToSamples(qpcTicks - _originQpc) + _windowOffset[_head];
            double raw = _lastDsp;
            double upper = raw + _maxLeadSamples;
            if (estimate > upper) estimate = upper;
            if (qpcTicks >= _lastQpc && estimate < raw) estimate = raw;

            dspClock = estimate;
            return true;
        }

        private void DropStaleAfterStep(long recentFrom)
        {
            while (_count > 1 && _windowQpc[_head] < recentFrom)
            {
                if (_windowOffset[_head] - RecentMax(recentFrom) <= _stepToleranceSamples) return;
                DropFront();
            }
        }

        // 덱은 시각 오름차순이므로 recentFrom 이후 첫 샘플이 최근 1초의 최댓값이다. 방금 넣은 샘플이 있어 항상 찾는다.
        private double RecentMax(long recentFrom)
        {
            for (int i = 0; i < _count; i++)
            {
                int slot = Slot(i);
                if (_windowQpc[slot] >= recentFrom) return _windowOffset[slot];
            }
            return _windowOffset[Slot(_count - 1)];
        }

        private double DspSinceOrigin(ulong dspClock)
        {
            // 부호 있는 차이로 바꿔 원점보다 작은 값도 다룬다.
            return (long)(dspClock - _originDsp);
        }

        private double QpcToSamples(long ticks)
        {
            return (double)ticks * _sampleRate / Qpc.Frequency;
        }

        private int Slot(int index)
        {
            return (_head + index) % Capacity;
        }

        private void DropFront()
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }
    }
}
