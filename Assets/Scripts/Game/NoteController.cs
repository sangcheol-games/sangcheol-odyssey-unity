using System;
using SCOdyssey.App;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    [RequireComponent(typeof(RectTransform))]
    public class NoteController : MonoBehaviour
    {
        public NoteData noteData { get; private set; }

        private Action<NoteController> onReturn;
        private Action<NoteController> onMissed;


        private bool isJudged = false;
        private RectTransform rectTransform;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        public void Init(NoteData noteData, Vector2 position, Action<NoteController> returnCallback, Action<NoteController> missCallback)
        {
            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.onMissed = missCallback;

            rectTransform.anchoredPosition = position;
            isJudged = false;
            SetVisual(noteData.noteType);
            gameObject.SetActive(true);

        }

        private void SetVisual(NoteType noteType)
        {
            // TODO: 노트 타입과 스킨에 따라 Sprite 설정
        }

        void FixedUpdate()
        {
            if (isJudged) return;

            float currentTime = GameManager.Instance.GetCurrentTime();
            float timeRemaining = noteData.time - currentTime;

            // TODO: 판정로직

            if (timeRemaining < -JUDGE_UHM) // Miss 판정
            {
                HandleMiss();
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

