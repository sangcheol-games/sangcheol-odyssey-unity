using System;
using SCOdyssey.Core;
using SCOdyssey.App;
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
        protected float holdWidth = 0f;
        protected bool isLTR;
        protected bool isJudged = false;
        protected bool isHoldRemaining = false;  // 판정 후 홀드바가 남아있는 상태
        protected NoteState currentState;
        protected TimelineController trackingTimeline;    // 감시할 타임라인
        protected RectTransform rectTransform;

        protected virtual void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null)
                noteImage = GetComponentInChildren<Image>();
        }

        public virtual void Init(NoteData noteData, Vector2 position, bool isLTR, float holdWidth, Action<NoteController> returnCallback)
        {
            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.rectTransform.anchoredPosition = position;
            this.isJudged = false;
            this.isHoldRemaining = false;
            this.isLTR = isLTR;
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
                    float ghostOpacity = 0.2f;
                    if (ServiceLocator.TryGet<ISettingsManager>(out var sm))
                        ghostOpacity = sm.Current.noteOpacity;
                    c.a = ghostOpacity;
                    break;
                case NoteState.Active:
                    c.a = 1f;
                    break;
            }
            noteImage.color = c;
            ApplyAlpha(c.a);
        }

        protected virtual void ApplyAlpha(float alpha) { }

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

            const float TIMELINE_OFFSET = 20f; // 판정선이 충분히 지나간 후 ghost로 전환하도록 여유 공간 설정

            if (trackingTimeline.isLTR)
            {
                if (timelineX > noteX + TIMELINE_OFFSET) isPassed = true;
            }
            else
            {
                if (timelineX < noteX - TIMELINE_OFFSET) isPassed = true;
            }

            if (isPassed)
            {
                SetState(NoteState.Ghost); // 판정선이 지나갔으니 고스트로 전환
                trackingTimeline = null; // 더 이상 감시 안 함
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
