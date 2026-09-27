using System;

namespace SCOdyssey.Audio.Clock
{
    // 앵커 커밋 계산. 시작과 재개가 같은 식을 쓴다.
    //   Z   = leadIn + audioOffsetMs / 1000        음원이 시작하는 곡 시각
    //   pos = τ0 − Z                               커밋할 곡 시각에서의 음원 위치(음수면 리드인이 남음)
    //   Cf  = p + lead                             곡 시계가 움직이기 시작하는 DSP
    //   S   = pos < 0 이면 Cf + round(−pos·R), 아니면 Cf   소리가 시작되는 DSP
    internal static class SongAnchor
    {
        public const double MinCommitLeadSeconds = 0.015;

        // 노트 싱크를 적용하는 유일한 곳. +면 음악이 늦게 시작한다. 실수 나눗셈이어야 한다.
        public static double AudioZero(double leadInSeconds, int audioOffsetMs)
        {
            return leadInSeconds + audioOffsetMs / 1000.0;
        }

        public static double AudioPosition(double songTime, double audioZero)
        {
            return songTime - audioZero;
        }

        // lead = max(3·L, 0.015·R) 샘플
        public static ulong CommitLead(int blockLength, int sampleRate)
        {
            ulong blocks = 3 * (ulong)blockLength;
            ulong minimum = (ulong)Math.Ceiling(MinCommitLeadSeconds * sampleRate);
            if (blocks > minimum) return blocks;
            return minimum;
        }

        // 음원 seek 위치(PCM 샘플, 음원 레이트 기준). 리드인이 남았으면 0.
        public static long SeekFrames(double audioPosition, int soundRate)
        {
            if (audioPosition <= 0) return 0;
            return (long)Math.Round(audioPosition * soundRate);
        }

        public static ulong SoundStartDsp(ulong clockStartDsp, double audioPosition, int sampleRate)
        {
            if (audioPosition >= 0) return clockStartDsp;
            return clockStartDsp + (ulong)Math.Round(-audioPosition * sampleRate);
        }
    }
}
