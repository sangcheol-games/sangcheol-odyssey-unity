using System;
using System.Collections;
using NUnit.Framework;
using SCOdyssey.App;
using UnityEngine;

namespace SCOdyssey.Game
{
    [RequireComponent(typeof(RectTransform))]
    public class TimelineController : MonoBehaviour
    {
        public RectTransform rectTransform;
        private CanvasGroup canvasGroup;

        private float startTime;      // 마디 시작 시간 (판정선 출발 시간)
        private float duration;       // 마디 길이 (이동에 걸리는 시간)
        private float startX;         // 출발 X 좌표 (UI 앵커 기준)
        private float endX;           // 도착 X 좌표

        public bool isLTR;            // 왼쪽에서 오른쪽으로 이동하는지 여부

        private Action<TimelineController> onReturn;

        private float screenBoundX; // 화면 경계 X 좌표
        private bool isRunning = false;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();

            screenBoundX = Screen.width / 2 + 100f; // TODO: 화면 밖으로 나가는 여유 공간 100px(임시값) -> 정확한 값은 캐릭터 애니메이션 적용 후 수정

        }

        public void Init(float startTime, float duration, float startX, float endX, Action<TimelineController> returnCallback)
        {
            this.startTime = startTime;
            this.duration = duration;
            this.startX = startX;
            this.endX = endX;
            this.onReturn = returnCallback;

            if (startX < endX)
                isLTR = true;
            else
                isLTR = false;

            rectTransform.anchoredPosition = new Vector2(startX, rectTransform.anchoredPosition.y);

            Activate();
            UpdatePosition();
        }

        void Update()
        {
            if (!isRunning) return;

            UpdatePosition();
        }

        private void UpdatePosition()
        {
            float currentTime = GameManager.Instance.GetCurrentTime();
            float elapsedTime = currentTime - startTime;
            float progress = elapsedTime / duration;

            // 보간 이동
            float currentX = Mathf.LerpUnclamped(startX, endX, progress);
            rectTransform.anchoredPosition = new Vector2(currentX, rectTransform.anchoredPosition.y);

            CheckOutOfBounds(currentX, progress);
        }

        private void CheckOutOfBounds(float currentX, float progress)
        {
            if (progress > 1.0f && Mathf.Abs(currentX) > screenBoundX)
            {
                ReturnToPool();
            }
        }

        private void Activate()
        {
            SetAlpha(1f);
            isRunning = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            ReturnToPool();
        }

        private void ReturnToPool()
        {
            isRunning = false;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }


        private void SetAlpha(float alpha)
        {
            canvasGroup.alpha = alpha;
        }
    }

}
