namespace SCOdyssey.Game
{
    // 홀드 꼬리 노트(채보 5): 헤드만 표시. 떼는 타이밍으로 판정한다(RhythmSession.Release)
    public class HoldReleaseNote : NoteController
    {
        protected override void SetVisual()
        {
            // 릴리즈 판정 노트: 헤드만 표시 (홀드바 없음)
            noteImage.enabled = true;
        }
    }
}
