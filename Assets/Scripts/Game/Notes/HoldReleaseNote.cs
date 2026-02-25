namespace SCOdyssey.Game
{
    public class HoldReleaseNote : NoteController
    {
        protected override void SetVisual()
        {
            // 릴리즈 판정 노트: 헤드만 표시 (홀드바 없음)
            noteImage.enabled = true;
            holdImage.enabled = false;
        }
    }
}
