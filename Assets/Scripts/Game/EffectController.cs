using UnityEngine;
using TMPro;
using System.Collections;
using static SCOdyssey.Domain.Service.Constants;
using System; // JudgeType 사용

namespace SCOdyssey.Game
{
    public class EffectController : MonoBehaviour
    {
        [Header("References")]
        public TextMeshProUGUI judgeText;
        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;

        private Action<EffectController> onReturn;

        [Header("Animation Settings")]
        public float floatSpeed = 100f; // 위로 올라가는 속도
        public float duration = 0.5f;   // 사라지는 데 걸리는 시간

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            rectTransform = GetComponent<RectTransform>();
        }

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
            switch (type)
            {
                case JudgeType.Perfect:
                    judgeText.text = "PERFECT";
                    judgeText.color = Color.cyan;
                    break;
                case JudgeType.Master:
                    judgeText.text = "MASTER";
                    judgeText.color = Color.cyan;
                    break;
                case JudgeType.Ideal:
                    judgeText.text = "IDEAL";
                    judgeText.color = Color.green;
                    break;
                case JudgeType.Kind:
                    judgeText.text = "KIND";
                    judgeText.color = Color.yellow;
                    break;
                case JudgeType.Uhm:
                    judgeText.text = "UHM..";
                    judgeText.color = Color.red;
                    break;
                default:
                    judgeText.text = "";
                    break;
            }
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
            judgeText.text = "";
            onReturn?.Invoke(this);
        }
    }
}