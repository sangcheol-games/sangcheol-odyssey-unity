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

        private Image noteImage;
        protected bool isJudged = false;
        private NoteState currentState;
        private TimelineController trackingTimeline;
        protected RectTransform rectTransform;

        protected virtual void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null) noteImage = GetComponent<Image>();
        }

        public virtual void Init(NoteData noteData, Vector2 position, Action<NoteController> returnCallback)
        {
            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.rectTransform.anchoredPosition = position;
            this.isJudged = false;

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
        }

        protected abstract void SetVisual();

        public void TrackTimeline(TimelineController timeline)
        {
            trackingTimeline = timeline;
            SetState(NoteState.Hidden);
        }

        void Update()
        {
            if (isJudged) return;

            if (currentState == NoteState.Hidden && trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
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
                }

            }

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