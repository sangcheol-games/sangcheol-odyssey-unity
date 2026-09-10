using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 홀드 시작 노트(채보 2): 일반 노트처럼 눌러 홀드에 진입. 헤드 + 홀드바(Bar)를 소유하며,
    // 판정선 위치에 맞춰 홀드바를 깎아낸다. 판정/miss 후에도 홀드바가 다 소모될 때까지 시각적으로 링거링.
    //
    // ── 홀드바 구조: 루트(RectMask2D 뷰포트) > Fill(실제 아트, Tiled 9-slice) ──────────────
    //  Fill은 항상 holdWidth 전체 길이로 깔려 홀드 끝단에 고정된다. 아트가 리사이즈되지 않으므로
    //  9-slice 캡과 타일 밀도가 holdWidth와 무관하게 원본 그대로 유지되고, 소모 중에도 패턴이 흐르지 않는다.
    //  소모는 루트(뷰포트)의 sizeDelta.x를 줄여 판정선 쪽에서 잘라내는 방식 — Filled의 fillAmount를 대체한다.
    //
    //  ⚠ 루트는 절대 회전시키지 않는다. RectMask2D의 clip rect는 코너 [0]·[2]만 써서 만들기 때문에
    //    Y 180° 회전이 들어가면 width가 음수가 되어 validRect가 false → 홀드바가 통째로 컬링된다.
    //    RTL은 피벗/앵커를 반대편으로 옮기고, 미러링은 Fill 쪽 회전으로 처리한다.
    //
    //  단위 주의: anchoredPosition은 부모(holdLayer, scale 1) 공간이라 화면 단위 그대로지만,
    //  sizeDelta는 자기 rect 공간이므로 화면 단위를 rectScale로 나눠 넣어야 한다.
    // ──────────────────────────────────────────────────────────────────────────
    public class HoldStartNote : NoteController
    {
        private Image holdImage;
        private RectTransform holdBarTransform;     // 루트: RectMask2D 뷰포트
        private RectTransform holdFillTransform;    // 자식 Fill: 항상 holdWidth 전체 길이인 아트
        private float barHeight;                    // 프리팹 rect 높이(스프라이트 자연 높이와 동일)
        private float rectScale;                    // 프리팹 localScale.x — 2배 제작 에셋 보정값
        private float remainingWidth;               // 남은 홀드바 폭(화면 단위). 기존 fillAmount 대체

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
            holdBarTransform = (RectTransform)holdBarObj.transform;

            // 아트는 반드시 자식 Fill에 있어야 한다(루트는 RectMask2D 뷰포트 전용).
            // GetComponentInChildren은 자기 자신도 포함하므로 쓰지 않는다 — 구버전(단일 GameObject)
            // 프리팹이 로드되면 루트 Image가 잡혀 뷰포트와 아트가 같은 트랜스폼이 되고,
            // 루트에 세로 스트레치 앵커가 걸려 홀드바 높이가 조용히 망가진다.
            holdImage = null;
            for (int i = 0; i < holdBarTransform.childCount; i++)
            {
                holdImage = holdBarTransform.GetChild(i).GetComponent<Image>();
                if (holdImage != null) break;
            }

            if (holdImage == null)
            {
                Debug.LogError("[HoldStartNote] HoldBarPrefab에 Fill 자식(Image)이 없습니다. " +
                               "프리팹이 구버전입니다 — Assets > Reimport 후 다시 실행하세요.", holdBarObj);
                holdBarTransform = null;    // SetVisual/UpdateHoldFill이 안전하게 빠지도록
                return;
            }

            holdFillTransform = holdImage.rectTransform;
            barHeight = holdBarTransform.sizeDelta.y;

            // 보정값은 프리팹을 단일 소스로 삼는다(코드에 0.5를 박지 않음)
            rectScale = holdBarTransform.localScale.x;
            if (Mathf.Approximately(rectScale, 0f)) rectScale = 1f;
        }

        protected override void SetVisual()
        {
            noteImage.enabled = true;

            // SetHoldBar 없이 들어오는 경로(ChartEditor 프리뷰)에서는 헤드만 표시하고 빠진다
            if (holdBarTransform == null) return;

            holdBarTransform.gameObject.SetActive(true);
            holdImage.enabled = true;

            // 풀 재사용 시 이전 노트의 방향이 남으므로 방향별 값은 분기 없이 항상 전부 세팅한다
            if (isLTR)
            {
                holdBarTransform.pivot = new Vector2(1f, 0.5f);
                holdFillTransform.anchorMin = new Vector2(1f, 0f);
                holdFillTransform.anchorMax = new Vector2(1f, 1f);
                holdFillTransform.localRotation = Quaternion.identity;
            }
            else
            {
                holdBarTransform.pivot = new Vector2(0f, 0.5f);
                holdFillTransform.anchorMin = new Vector2(0f, 0f);
                holdFillTransform.anchorMax = new Vector2(0f, 1f);
                holdFillTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            holdBarTransform.localRotation = Quaternion.identity;   // 마스크 루트는 무회전 고정
            holdFillTransform.pivot = new Vector2(1f, 0.5f);
            holdFillTransform.anchoredPosition = Vector2.zero;

            // 루트 피벗이 rect의 끝단이므로 Fill의 앵커 기준점은 루트 sizeDelta와 무관하게 항상 0이다.
            // 즉 루트(뷰포트)를 줄여도 Fill 아트는 1픽셀도 움직이지 않는다.
            float rectWidth = holdWidth / rectScale;
            holdFillTransform.sizeDelta = new Vector2(rectWidth, 0f);

            // 루트는 홀드 끝단에 배치(피벗이 끝단) → 판정선이 지나가는 근접측부터 잘려나간다
            Vector2 head = rectTransform.anchoredPosition;
            holdBarTransform.anchoredPosition = new Vector2(head.x + holdWidth * (isLTR ? 1f : -1f), head.y);

            remainingWidth = holdWidth;
            holdBarTransform.sizeDelta = new Vector2(rectWidth, barHeight);

            ApplyAlpha(1f);     // 풀 재사용 시 이전 노트의 알파 잔재 제거
        }

        // 판정 or Miss 시 시스템에서는 제거되지만, 홀드바 시각효과는 링거링으로 유지.
        // 노트 반환(DeleteNote)은 홀드바가 다 소모되는 시점에 Update가 담당하므로 여기서는 호출하지 않는다.
        public override void OnHit()
        {
            if (isJudged) return;
            isJudged = true;
            isHoldRemaining = true;     // 홀드바 잔여 표시 시작
            PlayHitAnim(() => noteImage.enabled = false);   // 히트 애니메이션을 보여준 뒤 헤드 숨기기
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

            // Active 상태에서도 타임라인이 지나가는 동안 홀드바를 미리 깎아둔다
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
                    if (remainingWidth <= 0f)
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
            if (holdBarTransform == null)
            {
                remainingWidth = 0f;    // 홀드바 없는 경로(에디터 프리뷰): 링거링 없이 즉시 소멸
                return;
            }

            float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;
            // 루트는 홀드 끝단에 있으므로 시작점 기준은 노트 헤드를 쓴다(홀드바 자신을 읽으면 안 됨)
            float holdStartX = rectTransform.anchoredPosition.x;

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

            remainingWidth = holdWidth - passedDistance;
            holdBarTransform.sizeDelta = new Vector2(remainingWidth / rectScale, barHeight);
        }
    }
}
