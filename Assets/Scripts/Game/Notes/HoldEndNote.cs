namespace SCOdyssey.Game
{
    public class HoldEndNote : NoteController
    {
        protected override void SetVisual()
        {
            // 끝점 플래그 전용 노트: 시각 표시 없음, 누르고 있는지 판정만 담당
            noteImage.enabled = false;
        }

        public override void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }
    }
}
