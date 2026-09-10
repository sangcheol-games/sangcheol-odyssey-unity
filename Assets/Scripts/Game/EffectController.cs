using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using static SCOdyssey.Domain.Service.Constants;
using System; // JudgeType 사용

namespace SCOdyssey.Game
{
    // 판정 이펙트(PERFECT/MASTER/... 스프라이트). ChartManager.EffectJudgement가 풀에서 꺼내 Setup으로 구동하고,
    // 애니메이션(위로 떠오르며 페이드아웃)이 끝나면 onReturn 콜백으로 풀에 반환된다.
    public class EffectController : MonoBehaviour
    {
        [Header("References")]
        public Image judgeImage;
        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;

        private Action<EffectController> onReturn;

        // GameUI_Effect_*. JudgeType enum 순서(Perfect, Master, Ideal, Kind, Umm)와 인덱스가 일치해야 함
        [SerializeField] private Sprite[] judgeSprites = new Sprite[5];

        [Header("Animation Settings")]
        public float floatSpeed = 100f; // 위로 올라가는 속도
        public float duration = 0.5f;   // 사라지는 데 걸리는 시간

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = GetComponent<RectTransform>();

            if (judgeImage == null)
                judgeImage = GetComponentInChildren<Image>(true);
            if (judgeImage == null)
                Debug.LogWarning("EffectController: No attached Image component");
        }

        // 판정 등급·표시 위치·반환 콜백을 받아 스프라이트를 세팅하고 떠오르는 애니메이션을 시작
        public void Setup(JudgeType type, Vector2 startPosition, Action<EffectController> returnCallback)
        {
            rectTransform.anchoredPosition = startPosition;
            onReturn = returnCallback;
            canvasGroup.alpha = 1f;

            SetStyle(type);

            gameObject.SetActive(true);
            StartCoroutine(AnimateRoutine());
        }

        private void SetStyle(JudgeType type)
        {
            if (judgeImage == null) return;

            int index = (int)type;

            if (index < 0 || index >= judgeSprites.Length || judgeSprites[index] == null)
            {
                judgeImage.enabled = false;
                return;
            }

            judgeImage.sprite = judgeSprites[index];
            judgeImage.enabled = true;
        }

        private IEnumerator AnimateRoutine()
        {
            float elapsed = 0f;
            Vector2 startPos = rectTransform.anchoredPosition + new Vector2(0f, 50f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;

                rectTransform.anchoredPosition = startPos + (Vector2.up * floatSpeed * elapsed);

                canvasGroup.alpha = 1f - progress;

                yield return null;
            }

            gameObject.SetActive(false);
            if (judgeImage != null)
                judgeImage.enabled = false;
            onReturn?.Invoke(this);
        }
    }
}
