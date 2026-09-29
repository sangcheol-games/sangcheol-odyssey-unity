namespace SCOdyssey.Rhythm
{
    // 게임 상대시간(초). 엔진을 모는 쪽과 뷰가 같은 시계를 읽는다
    public interface IRhythmClock
    {
        double Now { get; }
    }
}
