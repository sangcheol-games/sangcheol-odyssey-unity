using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 1건의 결과. NoteId로 뷰(NoteController)를 되찾는다.
    public readonly struct JudgeEvent
    {
        public readonly int NoteId;     // JudgeTrack 배열 인덱스 = NoteData.id
        public readonly Lane Lane;
        public readonly NoteType Kind;
        public readonly JudgeType Judge;
        public readonly bool IsMiss;    // 입력 없이 윈도우를 지나쳐 확정된 것

        public JudgeEvent(int noteId, Lane lane, NoteType kind, JudgeType judge, bool isMiss)
        {
            NoteId = noteId;
            Lane = lane;
            Kind = kind;
            Judge = judge;
            IsMiss = isMiss;
        }

        public static JudgeEvent Miss(int noteId, JudgeNote note)
            => new(noteId, note.Lane, note.Kind, JudgeType.Umm, true);

        public override string ToString()
            => $"#{NoteId} {Lane} {Kind} {Judge}{(IsMiss ? " (miss)" : "")}";
    }
}
