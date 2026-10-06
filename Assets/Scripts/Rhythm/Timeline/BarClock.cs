namespace SCOdyssey.Rhythm
{
    // 4/4 박자 마디 시계. 마디 길이는 파서가 노트 시각을 만들 때와 같은 float 계산으로 정한다
    public readonly struct BarClock
    {
        public const int BeatsPerBar = 4;

        public readonly double BarDuration;

        public BarClock(double barDuration)
        {
            BarDuration = barDuration;
        }

        // 4/4 박자만 다룬다
        public static BarClock FromBpm(int bpm) => new((60f / bpm) * BeatsPerBar);

        public double BeatDuration => BarDuration / BeatsPerBar;

        public double BarStart(int bar) => bar * BarDuration;
    }
}
