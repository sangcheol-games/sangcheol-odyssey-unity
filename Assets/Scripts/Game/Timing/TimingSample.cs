namespace SCOdyssey.Game.Timing
{
    public enum TimingKind
    {
        Press,
        HoldBody,
        Release,
        Miss
    }

    // 판정 한 건의 부호 있는 오차. 후속 캘리브레이션과 개발 오버레이가 쓴다.
    public readonly struct TimingSample
    {
        public readonly TimingKind Kind;
        public readonly int Grade;
        public readonly double ErrorMs;             // + = 늦음
        public readonly int JudgmentOffsetSteps;    // 래치한 판정 싱크 단계
        public readonly int Epoch;

        public TimingSample(TimingKind kind, int grade, double errorMs, int judgmentOffsetSteps, int epoch)
        {
            Kind = kind;
            Grade = grade;
            ErrorMs = errorMs;
            JudgmentOffsetSteps = judgmentOffsetSteps;
            Epoch = epoch;
        }
    }

    public interface IJudgementTimingLog
    {
        void Record(TimingKind kind, int grade, double errorMs);   // 판정 싱크 단계와 Epoch는 로그가 채운다
    }
}
