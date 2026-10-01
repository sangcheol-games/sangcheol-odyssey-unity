using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.UI
{
    /// <summary>
    /// 로딩 화면 장식의 반복 연출(회전·점멸). GameLoadingUI 프리팹 루트에 붙는다.
    /// 흐름(진행률·페이드·실패)은 GameLoadingUI가 맡고, 이 컴포넌트는 장식만 움직인다. 둘은 서로 모른다.
    ///
    /// UIManager가 인스턴스를 영구 캐시하므로 Awake는 생애 1회만 돈다. 트윈도 그때 한 번만 만든다.
    /// 숨김/재표시는 SetLink가 처리한다. UIManager.RefreshVisibility가 루트를 SetActive(false)하면 일시정지,
    /// 다시 켜면 멈춘 지점에서 이어 재생하고, 파괴되면 함께 정리된다.
    ///
    /// 로딩 중 1초짜리 프레임 직후에는 연출이 한 번에 건너뛴다. GameLoadingUI.UnscaledStep 같은 클램프가
    /// DOTween에는 없기 때문이다. 직전까지 화면이 멈춰 있던 상태라 체감이 작아 감수한다.
    /// (DOTween 전역 설정으로 맞추면 게임 쪽 트윈까지 바뀐다)
    /// </summary>
    [DisallowMultipleComponent]
    public class GameLoadingAnimator : MonoBehaviour
    {
        // 회전 장식 하나. 회전 중심은 target의 pivot이라, 그림의 시각적 중심에 pivot이 없으면 흔들리거나 공전한다.
        // public인 이유: private 중첩 타입의 public 필드는 인스펙터로만 채워져도 CS0649(할당 안 됨) 경고가 난다.
        [Serializable]
        public struct Spinner
        {
            public RectTransform target;

            [Tooltip("초당 회전 각도. 양수 = 반시계, 음수 = 시계. 360 ÷ 값 = 한 바퀴 시간(초)")]
            public float degreesPerSecond;
        }

        // 점멸 장식 하나. 프리팹에 저장된 알파(최대)와 minAlpha 사이를 부드럽게 오간다.
        // 알파만 바꾼다. 크기를 바꾸면 자식까지 함께 커진다(shadow는 LP 묶음의 부모다).
        [Serializable]
        public struct Blinker
        {
            public Graphic target;

            [Tooltip("가장 어두울 때의 알파(0~1). 최대 알파는 프리팹에 저장된 색의 알파")]
            [Range(0f, 1f)] public float minAlpha;

            [Tooltip("밝음 → 어두움 → 밝음 한 번에 걸리는 시간(초)")]
            public float period;
        }

        [SerializeField] private Spinner[] spinners;
        [SerializeField] private Blinker[] blinkers;

        private void Awake()
        {
            if (spinners != null)
            {
                foreach (Spinner spinner in spinners)
                {
                    StartSpin(spinner.target, spinner.degreesPerSecond);
                }
            }

            if (blinkers != null)
            {
                foreach (Blinker blinker in blinkers)
                {
                    StartBlink(blinker.target, blinker.minAlpha, blinker.period);
                }
            }
        }

        private void StartSpin(RectTransform target, float degreesPerSecond)
        {
            if (target == null || Mathf.Approximately(degreesPerSecond, 0f)) return;

            // 양수 Z = 반시계(AdventureUI의 "시계방향 = 음수 Z"와 같은 규칙).
            // SetRelative라 프리팹에서 잡아 둔 초기 각도에서 한 바퀴를 돌고, 끝 = 시작이라 Restart가 이음매 없이 이어진다.
            target.DOLocalRotate(new Vector3(0f, 0f, Mathf.Sign(degreesPerSecond) * 360f),
                                 360f / Mathf.Abs(degreesPerSecond), RotateMode.FastBeyond360)
                  .SetRelative(true)
                  .SetEase(Ease.Linear)
                  .SetLoops(-1, LoopType.Restart)
                  .SetUpdate(true)   // GameLoadingUI의 다른 연출처럼 unscaled
                  .SetLink(gameObject, LinkBehaviour.PauseOnDisablePlayOnEnable);
        }

        private void StartBlink(Graphic target, float minAlpha, float period)
        {
            if (target == null || period <= 0f) return;

            // 반 주기마다 방향을 바꾼다. 시작값이 프리팹 알파라 Yoyo가 그 값과 minAlpha 사이를 오간다.
            target.DOFade(minAlpha, period * 0.5f)
                  .SetEase(Ease.InOutSine)
                  .SetLoops(-1, LoopType.Yoyo)
                  .SetUpdate(true)
                  .SetLink(gameObject, LinkBehaviour.PauseOnDisablePlayOnEnable);
        }
    }
}
