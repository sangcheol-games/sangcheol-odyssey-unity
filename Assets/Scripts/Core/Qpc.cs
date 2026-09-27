using System;
using System.Diagnostics;

namespace SCOdyssey.Core
{
    // 벽시계 기준 하나. 입력, 프레임, 곡 시계의 시각 계산은 모두 QPC(Stopwatch) 틱으로 한다.
    public static class Qpc
    {
        public static readonly long Frequency = Stopwatch.Frequency;

        public static long Now
        {
            get { return Stopwatch.GetTimestamp(); }
        }

        public static double ToSeconds(long ticks)
        {
            return (double)ticks / Frequency;
        }

        public static long FromSeconds(double seconds)
        {
            return (long)Math.Round(seconds * Frequency);
        }
    }
}
