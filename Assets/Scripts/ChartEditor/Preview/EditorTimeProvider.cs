using System;

namespace SCOdyssey.ChartEditor.Preview
{
    /// <summary>
    /// 에디터 프리뷰용 시간 소스.
    /// FMOD DSP 클록(Func&lt;double&gt; 주입) 기반, 일시정지/재개/오프셋 지원.
    /// AudioSettings.dspTime 대신 외부 시간 소스를 주입받아 FMOD 통합 이후에도 정상 동작.
    /// </summary>
    public class EditorTimeProvider
    {
        private double startDspTime;        // 재생 시작 시점의 DSP 시간
        private double pausedElapsed;       // 일시정지 시점까지 경과한 시간
        private Func<double> _dspTimeSource; // 외부 시간 소스 (EditorFMODAudio.GetDSPTime)

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 재생 시작
        /// </summary>
        /// <param name="offset">시작 시간 오프셋 (초). 특정 마디부터 시작할 때 사용</param>
        /// <param name="dspTimeSource">DSP 시간 공급 함수 (EditorFMODAudio.GetDSPTime)</param>
        public void Start(double offset, Func<double> dspTimeSource)
        {
            _dspTimeSource = dspTimeSource;
            startDspTime = dspTimeSource();
            pausedElapsed = offset;  // offset을 초기값으로 포함하여 이후 GetCurrentTime에서 별도 가산 불필요
            IsPlaying = true;
            IsPaused = false;
        }

        /// <summary>
        /// 일시정지
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying || IsPaused) return;

            pausedElapsed = GetCurrentTime();
            IsPaused = true;
        }

        /// <summary>
        /// 재개
        /// </summary>
        public void Resume()
        {
            if (!IsPlaying || !IsPaused) return;

            startDspTime = _dspTimeSource();
            IsPaused = false;
        }

        /// <summary>
        /// 정지
        /// </summary>
        public void Stop()
        {
            IsPlaying = false;
            IsPaused = false;
        }

        /// <summary>
        /// 현재 경과 시간 반환 (오프셋 포함)
        /// </summary>
        public double GetCurrentTime()
        {
            if (!IsPlaying) return 0;

            if (IsPaused) return pausedElapsed;

            return (_dspTimeSource() - startDspTime) + pausedElapsed;  // timeOffset은 pausedElapsed에 포함됨
        }
    }
}
