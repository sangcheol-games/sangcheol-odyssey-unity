namespace SCOdyssey.Rhythm
{
    // 바깥이 프레임마다 Tick으로 모는 시계. 일시정지·배속, Jump로 한 번에 건너뛰기
    public sealed class ManualClock : IRhythmClock
    {
        private double _pendingJump;

        public double Now { get; private set; }
        public bool Paused { get; set; }
        public double Speed { get; set; } = 1.0;

        public ManualClock(double start = 0)
        {
            Now = start;
        }

        public void Tick(double realDeltaSec)
        {
            Now += (Paused ? 0 : realDeltaSec * Speed) + _pendingJump;
            _pendingJump = 0;
        }

        // 다음 Tick에 sec만큼 더 흐른다. 일시정지 중에도 흐른다
        public void Jump(double sec) => _pendingJump += sec;

        public void Reset(double start = 0)
        {
            Now = start;
            _pendingJump = 0;
        }
    }
}
