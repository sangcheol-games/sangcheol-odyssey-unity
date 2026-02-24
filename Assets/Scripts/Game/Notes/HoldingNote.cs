namespace SCOdyssey.Game
{
    public class HoldingNote : NoteController
    {
        protected override void SetVisual()
        {
            noteImage.enabled = false;
            holdImage.enabled = true;
            holdImage.fillAmount = 1f;
            holdImage.fillOrigin = 1;   // Right: fillAmount 감소 시 왼쪽(판정선 진입 방향)부터 소모
        }

        // 판정 시 시스템에서는 제거되지만, 홀드바 시각효과는 링거링으로 유지
        public override void OnHit()
        {
            if (isJudged) return;
            isJudged = true;
            isHoldRemaining = true;     // 홀드바 잔여 표시 시작
        }

        protected override void Update()
        {
            if (isHoldRemaining)
            {
                if (trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
                {
                    UpdateHoldFill();
                    if (holdImage.fillAmount <= 0f)
                    {
                        isHoldRemaining = false;
                        DeleteNote();
                    }
                }
                else
                {
                    // 타임라인이 이미 사라진 경우 즉시 삭제
                    isHoldRemaining = false;
                    DeleteNote();
                }
                return;
            }

            base.Update();
        }
    }
}
