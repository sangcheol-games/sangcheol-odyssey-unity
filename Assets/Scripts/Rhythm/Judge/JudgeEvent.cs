using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정 1건의 결과. NoteId로 뷰(NoteController)를 되찾는다.
    public readonly struct JudgeEvent
    {
        public readonly int NoteId;     // 판정 트랙 인덱스 = NoteData.id
        public readonly Lane Lane;
        public readonly NoteKind Kind;
        public readonly int PairId;     // 홀드 짝의 NoteId. 없으면 -1
        public readonly JudgeType Judge;
        public readonly bool IsMiss;
        public readonly double DeltaSec;   // 입력 시각 - 판정 시각(오프셋 포함). +면 늦음. 입력 없이 확정된 miss는 +윈도우
        public readonly double Time;       // 판정이 확정된 시각. 입력이면 입력 시각, 아니면 윈도우가 닫힌 시각

        public JudgeEvent(int noteId, JudgeNote note, JudgeType judge, bool isMiss, double deltaSec, double time)
        {
            NoteId = noteId;
            Lane = note.Lane;
            Kind = note.Kind;
            PairId = note.PairId;
            Judge = judge;
            IsMiss = isMiss;
            DeltaSec = deltaSec;
            Time = time;
        }

        public static JudgeEvent Hit(int noteId, JudgeNote note, JudgeType grade, double deltaSec, double time)
            => new(noteId, note, grade, false, deltaSec, time);

        public static JudgeEvent Miss(int noteId, JudgeNote note, double deltaSec, double time)
            => new(noteId, note, JudgeType.Umm, true, deltaSec, time);

        public override string ToString()
            => $"#{NoteId} {Lane} {Kind} {Judge}{(IsMiss ? " (miss)" : "")} {DeltaSec * 1000:+0.0;-0.0;0}ms @{Time:F3}";
    }
}
