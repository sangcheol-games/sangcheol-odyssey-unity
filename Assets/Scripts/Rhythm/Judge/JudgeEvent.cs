using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정 1건의 결과. NoteId로 뷰(NoteController)를 되찾는다.
    public readonly struct JudgeEvent
    {
        public readonly int NoteId;     // 판정 트랙 인덱스 = NoteData.id
        public readonly Lane Lane;
        public readonly NoteType Kind;
        public readonly JudgeType Judge;
        public readonly bool IsMiss;    // 입력 없이 윈도우를 지나쳐 확정된 것
        public readonly double DeltaSec;   // 입력 시각 - 판정 시각(오프셋 포함). +면 늦음. 입력 없는 miss는 +Umm
        public readonly double Time;       // 판정이 확정된 시각. 입력이면 입력 시각, miss면 윈도우가 닫힌 시각

        public JudgeEvent(int noteId, Lane lane, NoteType kind, JudgeType judge, bool isMiss, double deltaSec, double time)
        {
            NoteId = noteId;
            Lane = lane;
            Kind = kind;
            Judge = judge;
            IsMiss = isMiss;
            DeltaSec = deltaSec;
            Time = time;
        }

        public static JudgeEvent Miss(int noteId, JudgeNote note, double deltaSec, double time)
            => new(noteId, note.Lane, note.Kind, JudgeType.Umm, true, deltaSec, time);

        public override string ToString()
            => $"#{NoteId} {Lane} {Kind} {Judge}{(IsMiss ? " (miss)" : "")} {DeltaSec * 1000:+0.0;-0.0;0}ms @{Time:F3}";
    }
}
