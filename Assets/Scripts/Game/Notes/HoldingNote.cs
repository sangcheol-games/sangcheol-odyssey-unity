namespace SCOdyssey.Game
{
    // 홀드 중간점 노트(채보 3): 비주얼 없음. 판정 시점에 홀드를 유지 중인지만 판정(CheckHoldingBody)
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
