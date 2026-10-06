using System;

namespace SCOdyssey.Rhythm
{
    // 오디오 DSP 시계 기준 게임 상대시간. 시작 전 0, 일시정지 중엔 멈추고 재개하면 멈춘 만큼 원점을 민다.
    // DSP 소스는 밖에서 주입한다(게임: FMOD DSP 클럭).
    public sealed class GameplayClock : IRhythmClock
    {
        private readonly Func<double> _dspNow;
        private double _pausedDsp;
        private double _stoppedAt;
        private bool _stopped;

        public GameplayClock(Func<double> dspNow)
        {
            _dspNow = dspNow;
        }

        public double OriginDsp { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

        public double Now
        {
            get
            {
                if (_stopped) return _stoppedAt;
                if (!IsRunning) return 0;
                return (IsPaused ? _pausedDsp : _dspNow()) - OriginDsp;
            }
        }

        public void Start()
        {
            OriginDsp = _dspNow();
            IsRunning = true;
            IsPaused = false;
            _stopped = false;
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            _pausedDsp = _dspNow();
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;
            OriginDsp += _dspNow() - _pausedDsp;
            IsPaused = false;
        }

        // 그 시각에서 멈춘다
        public void Stop()
        {
            if (!IsRunning) return;
            _stoppedAt = Now;
            _stopped = true;
            IsRunning = false;
            IsPaused = false;
        }

        public double ToChartTime(double dsp) => dsp - OriginDsp;
    }
}
