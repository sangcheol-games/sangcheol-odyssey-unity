using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정 상태를 읽기만 하는 창. 뷰는 판정 엔진을 이것으로만 본다.
    public interface IJudgeStateReader
    {
        double Now { get; }
        int Count { get; }
        bool IsFinished { get; }

        NoteStatus StatusOf(int noteId);
        JudgeType? GradeOf(int noteId);   // Judged일 때만 값이 있다
        JudgeNote NoteAt(int noteId);
        bool IsHeld(Lane lane);           // 키가 눌려 있는가
        int HoldInProgressOf(Lane lane);  // 그 레인에서 누르는 중인 홀드의 꼬리 NoteId. 없으면 -1
    }
}
