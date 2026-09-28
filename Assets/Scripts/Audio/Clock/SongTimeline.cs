namespace SCOdyssey.Audio.Clock
{
    // 곡 시계의 한 구간. FromQpc부터 다음 세그먼트 전까지 유효하다.
    // 멈춘 구간은 곡 시각이 SongTimeAtStart로 고정되고, 흐르는 구간은 StartDsp(Cf)부터 흐른다.
    internal readonly struct ClockSegment
    {
        public readonly long FromQpc;
        public readonly int Epoch;
        public readonly bool IsRunning;
        public readonly double SongTimeAtStart;     // τ0
        public readonly double StartDsp;            // Cf. 흐르는 구간만
        public readonly int SampleRate;             // R. 흐르는 구간만
        public readonly int ModelVersion;           // StartDsp를 잰 DSP 도메인

        public ClockSegment(long fromQpc, int epoch, bool isRunning, double songTimeAtStart, double startDsp, int sampleRate, int modelVersion)
        {
            FromQpc = fromQpc;
            Epoch = epoch;
            IsRunning = isRunning;
            SongTimeAtStart = songTimeAtStart;
            StartDsp = startDsp;
            SampleRate = sampleRate;
            ModelVersion = modelVersion;
        }

        // 곡 시각 = τ0 + (DSP − Cf) / R. Cf 전(커밋 가드 구간)이면 τ0.
        public double SongTimeAt(double dsp)
        {
            if (!IsRunning) return SongTimeAtStart;
            if (dsp <= StartDsp) return SongTimeAtStart;
            return SongTimeAtStart + (dsp - StartDsp) / SampleRate;
        }
    }

    // 최근 세그먼트 몇 개를 보관한다. 프레임보다 조금 이른 입력이 이전 세그먼트에 속할 수 있기 때문이다.
    internal sealed class SongTimeline
    {
        public const int History = 8;

        private readonly ClockSegment[] _segments = new ClockSegment[History];
        private int _count;
        private int _latest;
        private int _epoch;

        public int Epoch
        {
            get { return _epoch; }
        }

        public bool HasSegment
        {
            get { return _count > 0; }
        }

        public ClockSegment Current
        {
            get { return _segments[_latest]; }
        }

        public void Freeze(double songTime, long fromQpc)
        {
            Add(new ClockSegment(fromQpc, _epoch + 1, false, songTime, 0, 0, 0));
        }

        public void Run(double songTimeAtStart, double startDsp, int sampleRate, int modelVersion, long fromQpc)
        {
            Add(new ClockSegment(fromQpc, _epoch + 1, true, songTimeAtStart, startDsp, sampleRate, modelVersion));
        }

        // qpc 시각에 유효한 세그먼트. 보관 범위보다 오래됐으면 false.
        public bool TryFind(long qpc, out ClockSegment segment)
        {
            for (int i = 0; i < _count; i++)
            {
                int index = (_latest - i + History) % History;
                if (_segments[index].FromQpc <= qpc)
                {
                    segment = _segments[index];
                    return true;
                }
            }
            segment = default;
            return false;
        }

        private void Add(ClockSegment segment)
        {
            _latest = (_latest + 1) % History;
            _segments[_latest] = segment;
            if (_count < History) _count++;
            _epoch = segment.Epoch;
        }
    }
}
