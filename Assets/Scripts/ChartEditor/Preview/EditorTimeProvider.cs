using UnityEngine;

namespace SCOdyssey.ChartEditor.Preview
{
    /// <summary>
    /// 에디터 프리뷰용 시간 소스.
    /// AudioSettings.dspTime 기반, 일시정지/재개/오프셋 지원.
    /// </summary>
    public class EditorTimeProvider
    {
        private double startDspTime;    // 재생 시작 시점의 dspTime
        private double pausedElapsed;   // 일시정지 시점까지 경과한 시간
        private double timeOffset;      // 재생 시작 오프셋 (특정 마디부터 시작 시)

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 재생 시작
        /// </summary>
        /// <param name="offset">시작 시간 오프셋 (초). 특정 마디부터 시작할 때 사용</param>
        public void Start(double offset = 0)
        {
            timeOffset = offset;
            startDspTime = AudioSettings.dspTime;
            pausedElapsed = 0;
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

            startDspTime = AudioSettings.dspTime;
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

            return (AudioSettings.dspTime - startDspTime) + pausedElapsed + timeOffset;
        }
    }
}
