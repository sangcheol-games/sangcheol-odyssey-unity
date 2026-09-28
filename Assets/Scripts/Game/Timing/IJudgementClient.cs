using SCOdyssey.Audio;

namespace SCOdyssey.Game.Timing
{
    // 곡 시각으로 바꾼 입력. 노트 시각과는 JudgeTime(판정 싱크 적용)만 비교한다.
    public readonly struct JudgedInput
    {
        public readonly int Lane;
        public readonly bool IsDown;
        public readonly long QpcTicks;
        public readonly double SongTime;
        public readonly double JudgeTime;
        public readonly bool Judgeable;         // 입력 시각에 곡 시계가 흐르고 있었고 Synthetic이 아님
        public readonly int Epoch;

        public JudgedInput(int lane, bool isDown, long qpcTicks, double songTime, double judgeTime, bool judgeable, int epoch)
        {
            Lane = lane;
            IsDown = isDown;
            QpcTicks = qpcTicks;
            SongTime = songTime;
            JudgeTime = judgeTime;
            Judgeable = judgeable;
            Epoch = epoch;
        }
    }

    // 매 프레임 OnLaneInput(시각순) → Advance → OnFrame 순서로 불린다.
    public interface IJudgementClient
    {
        void OnLaneInput(in JudgedInput input);
        void Advance(double songTime, double judgeTime);    // 마디 진행은 songTime, miss·홀드는 judgeTime
        void OnFrame(ISongSession session);
    }
}
