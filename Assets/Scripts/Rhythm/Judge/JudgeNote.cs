using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정 층의 노트 종류. 홀드 1개 = 머리(누르는 타이밍) + 꼬리(떼는 타이밍) 판정 2개
    public enum NoteKind
    {
        Tap,
        HoldHead,
        HoldTail,
    }

    public static class NoteKinds
    {
        // 채보 문자 1 -> Tap, 2 -> HoldHead, 4/5 -> HoldTail. 3(본체)은 판정 대상이 아니다
        public static bool TryFrom(NoteType type, out NoteKind kind)
        {
            switch (type)
            {
                case NoteType.Normal: kind = NoteKind.Tap; return true;
                case NoteType.HoldStart: kind = NoteKind.HoldHead; return true;
                case NoteType.HoldEnd:
                case NoteType.HoldRelease: kind = NoteKind.HoldTail; return true;
                default: kind = default; return false;
            }
        }
    }

    /// 판정 전용 노트. 파싱 시점에 값이 확정.
    public readonly struct JudgeNote
    {
        public readonly double Time;    // 판정 시각(게임 상대시간). LaneData.ConvertSequenceToNotes가 선계산한 값 그대로
        public readonly Lane Lane;      // 0-based. 채보파일의 1~4는 LaneMap.FromChartLine이 변환
        public readonly NoteKind Kind;
        public readonly int PairId;     // 홀드 머리 <-> 꼬리의 상대 인덱스. 탭이나 짝 없는 노트는 -1

        public JudgeNote(double time, Lane lane, NoteKind kind, int pairId = -1)
        {
            Time = time;
            Lane = lane;
            Kind = kind;
            PairId = pairId;
        }

        public JudgeNote WithPair(int pairId) => new(Time, Lane, Kind, pairId);

        public override string ToString() => $"{Time:F3}s {Lane} {Kind}{(PairId >= 0 ? $" <->#{PairId}" : "")}";
    }

    public enum NoteStatus
    {
        Pending,
        InProgress,   // 홀드 꼬리 전용: 머리가 판정됐고 키를 누르고 있다
        Judged,
        Missed,
    }
}
