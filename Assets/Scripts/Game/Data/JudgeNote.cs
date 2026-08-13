using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    /// 판정 전용 노트. 파싱 시점에 값이 확정.
    public readonly struct JudgeNote
    {
        public readonly double Time;    // 판정 시각(게임 상대시간). LaneData.ConvertSequenceToNotes가 선계산한 값 그대로
        public readonly Lane Lane;      // 0-based. 채보파일의 1~4는 LaneMap.FromChartLine이 변환
        public readonly NoteType Kind;

        public JudgeNote(double time, Lane lane, NoteType kind)
        {
            Time = time;
            Lane = lane;
            Kind = kind;
        }

        public override string ToString() => $"{Time:F3}s {Lane} {Kind}";
    }

    // 판정 상태
    public enum NoteStatus
    {
        Pending,
        Judged,
        Missed,
    }
}
