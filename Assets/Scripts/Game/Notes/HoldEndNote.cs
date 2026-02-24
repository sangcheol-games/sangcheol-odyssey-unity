namespace SCOdyssey.Game
{
    public class HoldEndNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = true;
            holdImage.enabled = false;  // 홀드바 없음: 바의 시각 효과는 HoldStart/HoldingNote가 담당
        }
    }
}
