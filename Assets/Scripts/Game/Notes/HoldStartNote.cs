using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class HoldStartNote : NoteController
    {
        private Image holdImage;
        private RectTransform holdBarTransform;

        protected override void ApplyAlpha(float alpha)
        {
            if (holdImage == null) return;
            Color c = holdImage.color;
            c.a = alpha;
            holdImage.color = c;
        }

        /// <summary>
        /// ChartManager에서 Init() 호출 전에 holdBar 오브젝트를 전달.
        /// </summary>
        public void SetHoldBar(GameObject holdBarObj)
        {
            holdImage = holdBarObj.GetComponent<Image>();
            holdBarTransform = holdBarObj.GetComponent<RectTransform>();
        }

        protected override void SetVisual()
        {
            holdBarTransform.gameObject.SetActive(true);
            noteImage.enabled = true;
            holdImage.enabled = true;
            holdImage.fillAmount = 1f;
            holdImage.fillOrigin = 1;   // Right: fillAmount 감소 시 왼쪽(판정선 진입 방향)부터 소모

            holdBarTransform.anchoredPosition = rectTransform.anchoredPosition;
            holdBarTransform.localRotation = isLTR ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            holdBarTransform.sizeDelta = new Vector2(holdWidth, holdBarTransform.sizeDelta.y);
        }

        // 판정 or Miss 시 시스템에서는 제거되지만, 홀드바 시각효과는 링거링으로 유지
        public override void OnHit()
        {
            if (isJudged) return;
            isJudged = true;
            noteImage.enabled = false;  // 헤드 숨기기
            isHoldRemaining = true;     // 홀드바 잔여 표시 시작
        }

        public override void OnMiss()
        {
            if (isJudged) return;
            isJudged = true;
            noteImage.enabled = false;  // 헤드 숨기기
            isHoldRemaining = true;     // miss여도 홀드바는 판정선이 지나갈 때까지 유지
        }

        protected override void Update()
        {
            base.Update();

            // Active 상태에서도 타임라인이 지나가는 동안 홀드바 fill을 미리 업데이트
            // (판정/miss 전부터 타임라인 위치에 맞춰 홀드바가 실시간으로 줄어들어야 함)
            if (!isHoldRemaining && currentState == NoteState.Active && trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
            {
                UpdateHoldFill();
            }

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
        }

        private void UpdateHoldFill()
        {
            float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;
            float holdStartX = holdBarTransform.anchoredPosition.x;

            float passedDistance = 0f;

            if (trackingTimeline.isLTR)
            {
                if (timelineX > holdStartX)
                    passedDistance = timelineX - holdStartX;
            }
            else
            {
                if (timelineX < holdStartX)
                    passedDistance = holdStartX - timelineX;
            }

            passedDistance = Mathf.Clamp(passedDistance, 0f, holdWidth);

            float fillRatio = 1f - (passedDistance / holdWidth);

            holdImage.fillAmount = fillRatio;
        }
    }
}
