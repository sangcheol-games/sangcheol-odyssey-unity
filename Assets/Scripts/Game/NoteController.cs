using System;
using SCOdyssey.App;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    [RequireComponent(typeof(RectTransform))]
    public class NoteController : MonoBehaviour
    {
        public NoteData noteData { get; private set; }

        private Action<NoteController> onReturn;
        private Action<NoteController> onMissed;

        public Image noteImage;
        private bool isJudged = false;
        public NoteState currentState;
        private TimelineController trackingTimeline;
        private RectTransform rectTransform;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null) noteImage = GetComponent<Image>();
        }

        public void Init(NoteData noteData, Vector2 position, Action<NoteController> returnCallback, Action<NoteController> missCallback)
        {
            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.onMissed = missCallback;

            rectTransform.anchoredPosition = position;
            isJudged = false;

            trackingTimeline = null;

            SetVisual(noteData.noteType);
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

        private void SetVisual(NoteType noteType)
        {
            // TODO: 노트 타입과 스킨에 따라 Sprite 설정
        }

        public void TrackTimeline(TimelineController timeline)
        {
            trackingTimeline = timeline;
            SetState(NoteState.Hidden);
        }

        void FixedUpdate()
        {
            if (isJudged) return;

            if (currentState == NoteState.Hidden && trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
            {
                float noteX = rectTransform.anchoredPosition.x;
                float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;

                bool isPased = false;

                if (trackingTimeline.isLTR)
                {
                    if (timelineX > noteX + 150f) isPased = true;    // 여유 공간 임시값 20f. 캐릭터 스프라이트 적용 후 조정 필요 
                }
                else
                {
                    if (timelineX < noteX - 150f) isPased = true;
                }

                if (isPased)
                {
                    SetState(NoteState.Ghost); // 판정선이 지나갔으니 고스트로 전환
                    trackingTimeline = null; // 더 이상 감시 안 함
                }

            }

            if (currentState == NoteState.Active)
            {
                float currentTime = GameManager.Instance.GetCurrentTime();
                float timeRemaining = noteData.time - currentTime;

                // TODO: 판정로직

                if (timeRemaining < -JUDGE_UHM) // Miss 판정
                {
                    HandleMiss();
                }
            }

        }
        
        private void HandleMiss()
        {
            isJudged = true;
            onMissed?.Invoke(this);
        }
        
        public void DeleteNote()
        {
            isJudged = true;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }

    }

}

