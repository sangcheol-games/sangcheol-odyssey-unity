namespace SCOdyssey.Game
{
    // 홀드 본체 노트(채보 3): 판정 대상이 아니라 게임에서는 스폰하지 않는다(NoteAdapter 호환용으로 남아 있음)
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
