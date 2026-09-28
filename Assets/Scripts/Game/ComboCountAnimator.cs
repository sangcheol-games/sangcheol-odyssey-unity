using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using TMPro;
using UnityEngine;

namespace SCOdyssey.Game
{
    // ── 흐름 (콤보 숫자 팝 연출) ──────────────────────────────────────────────
    //
    //  배치: GameScene의 Canvas/Information/Combo/ComboCount에 부착한다.
    //        같은 Combo 그룹의 형제인 Indicator에는 ComboIndicatorAnimator가 붙어 있고, 둘은 서로
    //        모르는 채 GameManager.UpdateCombo에서 나란히 호출된다.
    //
    //  모델: 진행도 _t 하나(0 = 시작 포즈, 1 = 정지 포즈)로 위치·크기·알파 세 채널을 동시에 굴린다.
    //          _t = 0   riseOffset만큼 아래 + startScale 배율 + startAlpha
    //          _t = 1   씬에 저장된 원래 위치·크기·알파(= 정지 포즈)
    //        정지 포즈는 하드코딩하지 않고 Awake에서 씬 값을 그대로 읽어 둔다. 그래야 "애니메이션이
    //        없는 상태" == "기존 화면 그대로"가 설계상 보장되고, 인스펙터에서 레이아웃을 옮겨도 따라간다.
    //
    //  구동: GameManager.UpdateCombo가 판정마다 SetCombo()를 호출한다(같은 값·콤보 0 재호출 포함).
    //        콤보가 실제로 증가했을 때만 팝을 처음부터 재시작한다. 인디케이터는 컨베이어라 "재조준"이
    //        맞지만, 이쪽은 매 노트의 타격감이 목적이라 연타 구간에서도 매번 0부터 다시 돈다.
    //        그래서 밀도 높은 구간(콤보 간격 75~150ms)에서는 숫자가 정지 포즈에 도달하지 못한 채
    //        작고 반투명한 쪽에 머문다 - startScale/startAlpha를 1에서 크게 떨어뜨리면 그 구간에서
    //        콤보 수를 읽기 어려워지므로 의도적으로 0.9 / 0.5에 둔다.
    //
    //  주의: ComboCount의 pivot은 (0.5, 0)이다. 스케일을 줄이면 아래쪽 변이 고정된 채 위에서 줄어들어
    //        글자의 시각적 중심이 아래로 내려간다. "아래에서 올라온다"는 의도와 방향이 같으므로
    //        pivot은 건드리지 않고, riseOffset은 그 효과에 얹히는 추가분 정도로만 준다.
    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 콤보가 오를 때마다 콤보 숫자가 살짝 아래에서 작고 반투명한 상태로 빠르게 솟아오르는 연출.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class ComboCountAnimator : MonoBehaviour
    {
        [Header("움직임")]
        [Tooltip("팝 한 번에 걸리는 시간. 연타 구간의 콤보 간격(75~150ms)보다 짧게 유지한다")]
        [SerializeField] private float popDuration = 0.1f;

        [Tooltip("솟아오르는 이징. 인디케이터의 stepEase와 같은 계열")]
        [SerializeField] private Ease popEase = Ease.OutCubic;

        [Header("시작 포즈 (정지 포즈로 수렴한다)")]
        [Tooltip("시작 시 아래로 내려가 있는 거리(px). fontSize 60 기준")]
        [SerializeField] private float riseOffset = 10f;

        [Tooltip("시작 배율. 연타 구간에서도 가독성이 남도록 1에서 크게 떨어뜨리지 않는다")]
        [SerializeField] private float startScale = 0.9f;

        [Tooltip("시작 알파. 위와 같은 이유로 너무 낮추지 않는다")]
        [SerializeField] private float startAlpha = 0.5f;

        private RectTransform _rect;
        private TextMeshProUGUI _text;

        // 정지 포즈. Awake에서 씬 값을 그대로 캡처한다
        private Vector2 _restPos;
        private Vector3 _restScale;
        private float _restAlpha;

        // Tweener가 아니라 구체 타입으로 잡는다. Tweener.ChangeStartValue는 첫 인자가 object라
        // 콤보마다 float가 박싱되지만, TweenerCore 쪽 오버로드는 T2(=float)를 그대로 받아 할당이 없다.
        private TweenerCore<float, float, FloatOptions> _popTween;
        private float _t;        // 진행도. 0 = 시작 포즈, 1 = 정지 포즈
        private int _lastCombo;  // 증가 판별용. OnComboChanged가 같은 값으로도 재발행되기 때문에 필요하다

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _text = GetComponent<TextMeshProUGUI>();

            if (_text == null)
            {
                Debug.LogError($"[{nameof(ComboCountAnimator)}] 같은 오브젝트에 TextMeshProUGUI가 없습니다. " +
                               $"콤보 숫자 텍스트에 부착하세요.", this);
            }

            // 정지 포즈는 씬 값이 정본이다. 여기서 캡처해 둔 값으로만 되돌아간다
            _restPos = _rect.anchoredPosition;
            _restScale = _rect.localScale;
            _restAlpha = _text != null ? _text.alpha : 1f;

            _popTween = DOTween.To(() => _t, v => _t = v, 1f, popDuration)
                .SetEase(popEase)
                .SetAutoKill(false)   // 콤보마다 재생성하지 않고 재사용한다(프로젝트 기본값이 자동 Kill이라 명시 필요)
                .SetLink(gameObject)  // 오브젝트 파괴 시 자동 Kill. 다시하기로 씬을 재로드할 때의 누수를 막는다
                .OnUpdate(Apply)
                .Pause();
        }

        private void OnEnable()
        {
            // 콤보가 끊기면 GameManager.UpdateCombo가 Combo 그룹을 통째로 껐다가 콤보 1에서 다시 켠다.
            // 다시 켜지는 순간 정지 포즈로 되돌려, 직전 연출의 잔상(작고 반투명한 상태)이 노출되지 않게 한다.
            ResetToRest();
        }

        private void OnDisable()
        {
            _popTween?.Pause();
        }

        // ─────────────────────────────────────────────
        // Public API (GameManager.UpdateCombo가 호출)
        // ─────────────────────────────────────────────

        /// <summary>
        /// 현재 콤보를 통보한다. OnComboChanged는 판정마다 같은 값으로도, 콤보 0으로도 재발행되므로
        /// "실제로 증가했는가"는 여기서 판단한다.
        /// </summary>
        public void SetCombo(int combo)
        {
            if (combo <= 0)
            {
                ResetToRest();
                return;
            }

            // 증가 폭이 얼마든 팝은 한 번이다(숫자는 한 번에 한 값만 보여주므로 밀어낼 대상이 없다)
            if (combo > _lastCombo)
                Play();

            _lastCombo = combo;
        }

        // ─────────────────────────────────────────────
        // Internals
        // ─────────────────────────────────────────────

        private void Play()
        {
            if (_popTween == null) return;

            // 세 줄이 각자 다른 일을 한다. 하나라도 지우면 조용히 망가진다.
            //  _t = 0f          DOTween은 시작값을 "첫 재생 시점의 getter 값"으로 캡처한다. ResetToRest가
            //                   _t를 1(정지 포즈)에 박아두므로, 이 줄이 없으면 첫 팝이 1→1이 되어 안 움직인다.
            //  ChangeStartValue 시작값을 0으로 못박아(hasManuallySetStartValue) 위의 캡처 타이밍에 의존하지
            //                   않게 만든다. 덤으로 popDuration을 넘기므로 플레이 중 인스펙터 수정이 바로 먹는다.
            //                   TweenerCore 쪽 오버로드라 콤보마다 박싱이 없다(문서상 NO-GC).
            //  Restart()        ChangeStartValue는 rewind만 할 뿐 멈춰 있는 트윈을 재생시키지는 않는다.
            //                   완료·일시정지 상태에서 실제로 다시 돌리려면 이게 있어야 한다.
            _t = 0f;
            _popTween.ChangeStartValue(0f, popDuration).Restart();
        }

        // 진행도를 1(정지 포즈)로 되돌리고 즉시 반영한다.
        // 순서 고정: Pause -> 필드 대입 -> Apply.
        //  - Pause가 먼저여야 재생 중인 트윈이 다음 업데이트에서 _t를 되돌려놓지 않는다.
        //  - Rewind()를 쓰면 안 된다. 트윈에 저장된 시작값을 setter로 다시 써서 _t를 0으로 덮는다.
        //  - _lastCombo를 안 되돌리면 콤보가 1부터 다시 쌓일 때 증가 판별이 거짓이 되어 연출이 먹통이 된다.
        private void ResetToRest()
        {
            _popTween?.Pause();

            _t = 1f;
            _lastCombo = 0;

            // 다음 팝 전까지 트윈이 돌지 않으므로, 여기서 안 그리면 직전 연출의 잔상이 계속 남는다
            Apply();
        }

        private void Apply()
        {
            if (_rect == null) return;

            float e = _t;   // 이징은 트윈이 이미 먹인 값이다

            // 위치·크기는 LerpUnclamped다. OutBack처럼 오버슈트가 있는 이징으로 바꿨을 때
            // 그 오버슈트가 그대로 살아야 연출이 의미가 있다.
            _rect.anchoredPosition = _restPos + new Vector2(0f, Mathf.LerpUnclamped(-riseOffset, 0f, e));
            _rect.localScale = _restScale * Mathf.LerpUnclamped(startScale, 1f, e);

            // 알파만 클램프한다. 오버슈트가 나면 1을 넘거나 음수가 되어 깜빡이기 때문
            if (_text != null)
                _text.alpha = Mathf.Lerp(startAlpha, _restAlpha, e);
        }
    }
}
