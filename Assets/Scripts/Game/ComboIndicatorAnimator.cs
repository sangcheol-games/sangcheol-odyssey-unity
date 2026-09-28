using System.Collections.Generic;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.Game
{
    // ── 흐름 (콤보 인디케이터 밀림 연출) ──────────────────────────────────────
    //
    //  배치: GameScene의 Canvas/Information/Combo/Indicator에 부착한다.
    //        자식 "링"(Indicator1~N)은 [래퍼 RectTransform + 좌/우 화살표 Image] 구조다.
    //        좌우 화살표는 래퍼 양 끝에 앵커·피벗이 맞물려 있으므로, 래퍼의 sizeDelta.x만 키우면
    //        화살표 한 쌍이 콤보 숫자를 기준으로 대칭으로 밀려난다. 이게 유일한 애니메이션 채널이다.
    //
    //  모델: 위상(phase) 하나로 전체를 굴리는 컨베이어. 링 i는 정거장 v = Repeat(_phase + i + 1, RING_COUNT)에
    //        앉고, 폭·알파를 v로 커브에서 뽑는다.
    //          v = 0            생성점 (폭 200, 알파 0)  ← 안쪽에서 새로 태어나는 자리
    //          v = 1, 2, 3      기존 정지 배치 (300·α1.0, 400·α0.5, 500·α0.2)
    //          v → RING_COUNT   소멸점 (폭 600, 알파 0)
    //        양 끝 알파가 0이라, v가 RING_COUNT에서 0으로 되감길 때의 폭 점프(600→200)가 보이지 않는다.
    //        +1 오프셋이 있어야 위상 0에서 씬의 Indicator1/2/3이 v=1,2,3(300/400/500)에 그대로 앉는다.
    //        (위상이 진행되면 링의 역할은 순환하므로 Indicator1~4라는 이름은 초기 배치만 뜻한다)
    //
    //  구동: GameManager.UpdateCombo가 판정마다 SetCombo()를 호출한다(같은 값·콤보 0 재호출 포함).
    //        콤보가 실제로 증가했을 때만 _targetPhase를 정수 한 칸 올리고, 트윈을 죽였다 새로 만드는 대신
    //        ChangeEndValue로 재조준한다. 밀도 높은 채보는 콤보 간격이 75~150ms라 판정마다 트윈을 새로
    //        걸면 서로 끊어 먹는다. 재조준 방식이라 연타 구간에서는 흐름이 끊기지 않고 속도만 빨라진다.
    //
    //  정지 상태: _targetPhase는 항상 정수라, 연출이 멈추면 링이 정확히 원래 배치(300/400/500)에 안착한다.
    //        즉 "애니메이션이 없는 상태" == "기존 화면 그대로"가 설계상 보장된다.
    //
    //  주의: 링 개수를 RING_COUNT로 맞추느라 Awake에서 템플릿(Indicator1)을 복제하고, 모든 링의 스프라이트를
    //        불투명 원본(_1)으로 통일한다. 따라서 에디터에서 보이는 모습(3링, _2/_3 스프라이트)과
    //        실행 화면(4링, 전부 _1 + 런타임 알파)이 다르다.
    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 콤보가 오를 때마다 인디케이터 화살표를 바깥으로 밀어내고, 안쪽에서 새 화살표가 자리를 채우는 연출.
    /// </summary>
    [DisallowMultipleComponent]
    public class ComboIndicatorAnimator : MonoBehaviour
    {
        // 링 하나 = 래퍼 RectTransform + 그 밑의 화살표 Image들(자식 순서 0:좌, 1:우)
        private struct Ring
        {
            public RectTransform rect;
            public Image[] arrows;
        }

        // 보이는 화살표 3쌍 + 생성 대기 1쌍. 아래 커브의 키 위치와 묶여 있으므로 인스펙터에 노출하지 않는다
        private const int RING_COUNT = 4;

        [Header("움직임")]
        [Tooltip("화살표가 한 칸 밀려나는 데 걸리는 시간")]
        [SerializeField] private float stepDuration = 0.2f;

        [Tooltip("밀려나는 이징. 연타 구간에서 이음매가 울컥거리면 OutSine이나 Linear로 낮춘다")]
        [SerializeField] private Ease stepEase = Ease.OutCubic;

        [Header("정거장 커브 (x축 = 정거장 번호 0 ~ 4)")]
        [Tooltip("정거장별 래퍼 폭(sizeDelta.x). 기본값은 기존 배치 300/400/500에 생성·소멸점을 덧붙인 것")]
        [SerializeField]
        private AnimationCurve widthCurve = new AnimationCurve(
            new Keyframe(0f, 200f, 100f, 100f),
            new Keyframe(1f, 300f, 100f, 100f),
            new Keyframe(2f, 400f, 100f, 100f),
            new Keyframe(3f, 500f, 100f, 100f),
            new Keyframe(4f, 600f, 100f, 100f));

        [Tooltip("정거장별 화살표 알파. 양 끝(생성·소멸)이 0이어야 되감기는 순간이 보이지 않는다")]
        [SerializeField]
        private AnimationCurve alphaCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1.0f),
            new Keyframe(1f, 1.0f, 0f, 0f),
            new Keyframe(2f, 0.5f, -0.4f, -0.4f),
            new Keyframe(3f, 0.2f, -0.25f, -0.25f),
            new Keyframe(4f, 0f, -0.2f, 0f));

        private Ring[] _rings;

        // Tweener가 아니라 구체 타입으로 잡는다. Tweener.ChangeEndValue는 첫 인자가 object라
        // 콤보마다 float가 박싱되지만, TweenerCore 쪽 오버로드는 T2(=float)를 그대로 받아 할당이 없다.
        private TweenerCore<float, float, FloatOptions> _phaseTween;
        private float _phase;        // 현재 위상
        private float _targetPhase;  // 목표 위상. 항상 정수다(정지 시 원래 배치에 안착하는 근거)
        private int _lastCombo;      // 증가 판별용. OnComboChanged가 같은 값으로도 재발행되기 때문에 필요하다

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Awake()
        {
            BuildRings();

            _phaseTween = DOTween.To(() => _phase, v => _phase = v, 0f, stepDuration)
                .SetEase(stepEase)
                .SetAutoKill(false)   // 콤보마다 재생성하지 않고 재사용한다(프로젝트 기본값이 자동 Kill이라 명시 필요)
                .SetLink(gameObject)  // 오브젝트 파괴 시 자동 Kill. 다시하기로 씬을 재로드할 때의 누수를 막는다
                .OnUpdate(ApplyRings)
                .Pause();
        }

        private void OnEnable()
        {
            // 콤보가 끊기면 GameManager.UpdateCombo가 Combo 그룹을 통째로 껐다가 콤보 1에서 다시 켠다.
            // 다시 켜지는 순간 정지 상태로 되돌려, 직전 연출의 잔상이 노출되지 않게 한다.
            ResetToRest();
        }

        private void OnDisable()
        {
            _phaseTween?.Pause();
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

            if (combo > _lastCombo)
                Push(combo - _lastCombo);

            _lastCombo = combo;
        }

        // ─────────────────────────────────────────────
        // Internals
        // ─────────────────────────────────────────────

        // 목표를 한 칸(또는 steps 칸) 올리고 트윈을 재조준한다.
        // snapStartValue: true라 시작값이 현재 위상이 되어 위치가 연속이고, 목표가 더 멀어진 채로 같은
        // duration을 쓰므로 연타가 몰아칠수록 자연히 빨라진다.
        private void Push(int steps)
        {
            if (_rings == null || _phaseTween == null || steps <= 0) return;

            _targetPhase += steps;

            // ChangeEndValue는 그 자체로 rewind까지 하지만 완료·일시정지 상태에서는 재생을 시작하지 않는다.
            // Restart()가 있어야 실제로 다시 돈다 - 중복처럼 보여도 지우면 안 된다.
            _phaseTween.ChangeEndValue(_targetPhase, stepDuration, true).Restart();
        }

        // 위상을 0으로 되돌리고 정지 배치를 즉시 반영한다.
        // 순서 고정: Pause -> 필드 대입 -> ApplyRings.
        //  - Pause가 먼저여야 재생 중인 트윈이 다음 업데이트에서 _phase를 되돌려놓지 않는다.
        //  - Rewind()를 쓰면 안 된다. 트윈에 저장된 시작값을 setter로 다시 써서 _phase를 낡은 값으로 덮는다.
        //    다음 Push가 snapStartValue로 현재 _phase(=0)를 읽어 가므로 Pause + 직접 대입으로 충분하다.
        //  - _lastCombo를 안 되돌리면 콤보가 1부터 다시 쌓일 때 증가 판별이 거짓이 되어 연출이 먹통이 된다.
        private void ResetToRest()
        {
            _phaseTween?.Pause();

            _phase = 0f;
            _targetPhase = 0f;
            _lastCombo = 0;

            // 다음 밀어내기 전까지 트윈이 돌지 않으므로, 여기서 안 그리면 직전 연출의 잔상이 계속 남는다
            ApplyRings();
        }

        private void ApplyRings()
        {
            if (_rings == null) return;

            for (int i = 0; i < _rings.Length; i++)
            {
                // +1 오프셋: 위상 0에서 씬의 Indicator1/2/3이 v=1,2,3(300/400/500)에 그대로 앉게 한다
                float v = Mathf.Repeat(_phase + i + 1, RING_COUNT);

                RectTransform rect = _rings[i].rect;
                Vector2 size = rect.sizeDelta;
                size.x = widthCurve.Evaluate(v);
                rect.sizeDelta = size;   // y는 상하 스트레치 앵커가 쓰므로 건드리지 않는다

                float alpha = Mathf.Clamp01(alphaCurve.Evaluate(v));
                Image[] arrows = _rings[i].arrows;
                for (int j = 0; j < arrows.Length; j++)
                {
                    Color c = arrows[j].color;
                    c.a = alpha;
                    arrows[j].color = c;
                }
            }
        }

        // 씬의 자식 링을 그대로 재사용하고, RING_COUNT에 모자란 만큼만 템플릿을 복제해 채운다.
        // 스프라이트는 전부 템플릿(Indicator1 = 불투명 _1 쌍)으로 통일한다.
        // 원래 투명도 3단계는 PNG(_1/_2/_3)에 구워져 있었지만, 컨베이어에서는 링 하나가 세 단계를 모두
        // 통과하므로 구워진 알파를 쓸 수 없다. 하나라도 _2/_3가 남으면 구워진 알파에 런타임 알파가 곱해져
        // 정지 포즈가 조용히 틀어진다.
        private void BuildRings()
        {
            if (_rings != null) return;

            RectTransform template = transform.childCount > 0 ? transform.GetChild(0) as RectTransform : null;
            if (template == null)
            {
                Debug.LogError($"[{nameof(ComboIndicatorAnimator)}] 링으로 쓸 자식 RectTransform이 없습니다. " +
                               $"Indicator1을 자식으로 두세요.", this);
                return;
            }

            // 템플릿 자신도 아래 루프에서 덮어써지므로 스프라이트 원본을 미리 읽어 둔다
            Image[] templateArrows = template.GetComponentsInChildren<Image>(true);

            // worldPositionStays: false여야 RectTransform의 앵커/anchoredPosition이 그대로 보존된다
            while (transform.childCount < RING_COUNT)
            {
                RectTransform clone = Instantiate(template, transform, false);
                clone.name = $"Indicator{transform.childCount}";
            }

            var rings = new List<Ring>(RING_COUNT);
            for (int i = 0; i < transform.childCount && rings.Count < RING_COUNT; i++)
            {
                RectTransform child = transform.GetChild(i) as RectTransform;
                if (child == null) continue;

                // 래퍼에는 Image가 없고 자식 좌/우 화살표만 잡힌다(깊이 우선이므로 순서는 0:좌, 1:우)
                Image[] arrows = child.GetComponentsInChildren<Image>(true);
                for (int j = 0; j < arrows.Length; j++)
                {
                    if (j < templateArrows.Length)
                        arrows[j].sprite = templateArrows[j].sprite;

                    arrows[j].raycastTarget = false;   // 장식용이므로 레이캐스트 대상에서 제외
                }

                rings.Add(new Ring { rect = child, arrows = arrows });
            }

            _rings = rings.ToArray();
        }
    }
}
