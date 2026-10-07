using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 누름 입력 한 번이 무엇을 쳤는지. 타격음과 캐릭터 연출이 같은 값을 본다.
    // Hit가 false면 헛침(칠 노트가 없거나 윈도우 밖). 이 입력 전에 확정된 miss는 여기 담기지 않는다.
    public readonly struct PressOutcome
    {
        public readonly bool Hit;
        public readonly int NoteId;
        public readonly NoteKind Kind;
        public readonly JudgeType Judge;

        public PressOutcome(int noteId, NoteKind kind, JudgeType judge)
        {
            Hit = true;
            NoteId = noteId;
            Kind = kind;
            Judge = judge;
        }

        public static PressOutcome NoTarget => default;

        public override string ToString() => Hit ? $"#{NoteId} {Kind} {Judge}" : "NoTarget";
    }
}
