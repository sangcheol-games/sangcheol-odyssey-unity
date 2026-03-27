namespace SCOdyssey.Game
{
    public class HoldingNote : NoteController
    {
        protected override void SetVisual()
        {
            // 홀딩 판정 전용 노트: 시각 표시 없음 (홀드바는 HoldStart가 전담)
            noteImage.enabled = false;
        }

        public override void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }
    }
}
