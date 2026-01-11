using System;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public abstract class NoteController : MonoBehaviour
    {
        public NoteData noteData { get; private set; }
        protected Action<NoteController> onReturn;

        protected Image noteImage;
        protected Image holdImage;
        protected float holdWidth = 0f;
        protected bool isJudged = false;
        protected NoteState currentState;
        protected TimelineController trackingTimeline;    // 감시할 타임라인
        protected RectTransform rectTransform;

        protected virtual void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null || holdImage == null)     // 이미지 컴포넌트 자동 할당
            {
                Image[] childs = GetComponentsInChildren<Image>();
                holdImage = childs[0];
                noteImage = childs[1];
            }
        }

        public virtual void Init(NoteData noteData, Vector2 position, bool isLTR, float holdWidth, Action<NoteController> returnCallback)
        {
            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.rectTransform.anchoredPosition = position;
            this.isJudged = false;

            if (!isLTR) holdImage.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            else holdImage.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            
            this.holdWidth = holdWidth;

            trackingTimeline = null;

            SetVisual();
            gameObject.SetActive(true);
        }

        public void SetState(NoteState state)
        {
            currentState = state;
            Color c = noteImage.color;

            switch (state)
            {
                case NoteState.Hidden:
                    c.a = 0f;
                    break;
                case NoteState.Ghost:
                    c.a = 0.05f;
                    break;
                case NoteState.Active:
                    c.a = 1f;
                    break;
            }
            noteImage.color = c;
            holdImage.color = c;
        }

        protected abstract void SetVisual();

        public void TrackTimeline(TimelineController timeline)
        {
            trackingTimeline = timeline;
        }

        protected virtual void Update()
        {
            if (isJudged) return;

            if (currentState == NoteState.Hidden && trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
            {
                CheckGhostState();
            }

        }

        protected void CheckGhostState()
        {
            float noteX = rectTransform.anchoredPosition.x;
            float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;

            bool isPassed = false;

            if (trackingTimeline.isLTR)
            {
                if (timelineX > noteX + 150f) isPassed = true;    // 여유 공간 임시값 20f. 캐릭터 스프라이트 적용 후 조정 필요 
            }
            else
            {
                if (timelineX < noteX - 150f) isPassed = true;
            }

            if (isPassed)
            {
                SetState(NoteState.Ghost); // 판정선이 지나갔으니 고스트로 전환
                trackingTimeline = null; // 더 이상 감시 안 함
                Debug.Log("노트 고스트 상태로 전환");
            }
        }

        protected void UpdateHoldFill()
        {
            float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;
            float holdEndX = rectTransform.anchoredPosition.x;

            float passedDistance = 0f;

            if (trackingTimeline.isLTR)
            {
                if (timelineX > holdEndX - holdWidth)
                    passedDistance = timelineX - holdEndX + holdWidth;
            }
            else
            {
                if (timelineX < holdEndX + holdWidth)
                    passedDistance = holdEndX + holdWidth - timelineX;
            }

            float fillRatio = 1f - (passedDistance / holdWidth);

            Debug.Log($"fillRatio: {fillRatio}");
            
            holdImage.fillAmount = Mathf.Clamp01(fillRatio);
        }

        public virtual void OnMiss()
        {
            if (isJudged) return;
            DeleteNote();
        }

        public virtual void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }

        public void DeleteNote()
        {
            isJudged = true;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }
    }
}