using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.Audio;
using SCOdyssey.Core;

namespace SCOdyssey.UI
{
    /// <summary>
    /// 결과 화면 카운트업 연출. 줄들이 위에서부터 차례로 0 → 최종값까지 올라가고 마지막에 도장들이 동시에 찍힌다.
    /// ResultUI 프리팹 루트에 붙는다.
    /// 무엇을 보여줄지(텍스트·값·표시 형식·도장 스프라이트·어떤 도장을 찍을지)는 ResultUI가 정해 Play로 넘기고,
    /// 이 컴포넌트는 그것을 어떻게 움직일지만 맡는다. ResultUI를 알지 못한다.
    ///
    /// UIManager가 인스턴스를 영구 캐시하므로 다시하기마다 Play가 재호출된다.
    /// 매번 이전 시퀀스를 Kill하고 정지 포즈로 되돌린 뒤 새로 만든다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResultCountAnimator : MonoBehaviour
    {
        // 카운트업 한 줄
        public readonly struct Row
        {
            public readonly TMP_Text Text;
            public readonly float Target;
            public readonly Func<float, string> Format;   // 진행 중 값 → 표시 문자열
            public readonly bool IsFinale;                // 마지막 클라이맥스 줄(점수). finaleDuration으로 길게 올라간다
            public readonly bool StartsGroup;             // 새 그룹의 첫 줄. 앞 줄들이 다 끝난 뒤 groupGap만큼 쉬고 시작한다

            public Row(TMP_Text text, float target, Func<float, string> format, bool isFinale = false, bool startsGroup = false)
            {
                Text = text;
                Target = target;
                Format = format;
                IsFinale = isFinale;
                StartsGroup = startsGroup;
            }
        }

        // 찍을 도장 하나. Show인 도장은 모두 동시에 찍는다. Show가 false면 이번 판에는 숨긴 채로 둔다 (예: 신기록이 아닐 때 NEW RECORD)
        public readonly struct Stamp
        {
            public readonly Image Image;
            public readonly bool Show;

            public Stamp(Image image, bool show)
            {
                Image = image;
                Show = show;
            }
        }

        // 도장의 정지 포즈 (프리팹 값)
        private struct StampRest
        {
            public Vector3 Scale;
            public Color Color;
        }

        // 카운트업 연출: 위에서부터 차례로 0 → 최종값까지 올라가고 마지막에 등급 도장이 찍힌다
        [Header("카운트 연출")]
        [Tooltip("판정·콤보·게이지 한 줄이 0에서 최종값까지 올라가는 시간(초)")]
        [SerializeField] private float rowDuration = 0.35f;      // 한 줄이 올라가는 시간
        [Tooltip("다음 줄이 시작하는 간격(초). '한 줄 시간'보다 짧으면 줄끼리 겹친다. 같으면 완전 순차, 0이면 전부 동시")]
        [SerializeField] private float rowStagger = 0.12f;       // 다음 줄이 시작하는 간격 (rowDuration보다 짧으면 겹친다)
        [Tooltip("그룹 사이 쉬는 시간(초). 앞 그룹(Perfect~Umm)의 마지막 줄이 다 끝난 뒤 이만큼 쉬고 다음 그룹(Rate~Score)을 시작한다. 0이면 앞 그룹이 끝나자마자 시작")]
        [SerializeField] private float groupGap = 0.2f;          // 판정 그룹과 Rate~Score 그룹 사이의 텀
        [Tooltip("마지막 줄(점수)이 올라가는 시간(초). 클라이맥스라 다른 줄보다 길게 둔다")]
        [SerializeField] private float finaleDuration = 0.8f;    // 점수 줄은 클라이맥스라 길게
        [Tooltip("숫자가 올라가는 속도 곡선. OutCubic = 처음엔 빠르게 올라가다 끝에서 천천히 멈춘다")]
        [SerializeField] private Ease countEase = Ease.OutCubic;
        [Tooltip("줄이 끝날 때 숫자가 커지는 정도. 0.2 = 원래 크기의 120%까지 커졌다 돌아온다. 0이면 펀치 없음")]
        [SerializeField] private float punchScale = 0.2f;        // 줄 완료 시 텍스트 펀치 크기
        [Tooltip("줄 완료 펀치가 커졌다 돌아오는 데 걸리는 시간(초)")]
        [SerializeField] private float punchDuration = 0.2f;
        [Tooltip("마지막 줄(점수)이 끝난 뒤 등급 도장이 찍히기까지 기다리는 시간(초)")]
        [SerializeField] private float stampDelay = 0.15f;       // 마지막 줄 완료 후 도장까지 대기
        [Tooltip("도장이 처음 나타날 때의 크기 배율. 이 크기에서 원래 크기로 내리꽂힌다. 클수록 위에서 떨어지는 느낌이 강하다")]
        [SerializeField] private float stampStartScale = 2.5f;   // 도장이 이 배율에서 원래 크기로 내리꽂힌다
        [Tooltip("도장이 원래 크기로 내려앉는 시간(초). 짧을수록 '쾅' 하고 세게 찍힌다. 페이드인은 이 시간의 절반")]
        [SerializeField] private float stampDuration = 0.25f;
        [Tooltip("도장이 내려앉는 속도 곡선. InQuad = 점점 빨라지며 착지해 찍히는 느낌")]
        [SerializeField] private Ease stampEase = Ease.InQuad;

        [Header("효과음")]
        [Tooltip("숫자가 바뀔 때 재생할 틱 효과음 파일명. StreamingAssets/Sfx 폴더에 둔다. 파일이 없으면 소리 없이 연출된다")]
        [SerializeField] private string tickSoundFile = "result_tick.wav";  // StreamingAssets/Sfx/ 기준, 효과음 볼륨을 따른다
        [Tooltip("틱 효과음 사이의 최소 간격(초). 여러 줄이 겹쳐 올라가도 이 간격보다 자주 울리지 않는다. 작을수록 촘촘한 '띠리리릭'")]
        [SerializeField] private float tickMinInterval = 0.03f;             // 줄이 겹쳐도 틱이 뭉개지지 않게 전역 쓰로틀
        [Tooltip("도장이 착지할 때 재생할 효과음 파일명. StreamingAssets/Sfx 폴더에 둔다. 도장 여러 개가 동시에 찍혀도 한 번만 울린다. 파일이 없으면 소리 없이 연출된다")]
        [SerializeField] private string stampSoundFile = "result_stamp.wav";

        private Sequence sequence;

        // 정지 포즈는 프리팹 값이 정본이다. 처음 Play될 때(아직 아무것도 움직이기 전) 캡처해 두고 매번 여기로 되돌린다
        private readonly Dictionary<RectTransform, Vector3> textRestScales = new Dictionary<RectTransform, Vector3>();
        private readonly Dictionary<Image, StampRest> stampRests = new Dictionary<Image, StampRest>();

        private ISfxPlayer oneShots;
        private OneShotId tickSound;
        private OneShotId stampSound;
        private float lastTickTime = float.NegativeInfinity;

        private void Awake()
        {
            // 파일이 없으면 Register가 경고만 하고 None을 돌려주며, Play는 None을 무시한다
            if (ServiceLocator.TryGet<ISfxPlayer>(out oneShots))
            {
                tickSound = oneShots.Register(tickSoundFile);
                stampSound = oneShots.Register(stampSoundFile);
            }
        }

        private void OnDisable()
        {
            // 연출 도중 화면을 나가면 정리한다. 다음 Play가 전부 초기화하므로 완료 처리는 필요 없다
            KillSequence();
        }

        /// <summary>
        /// rows를 순서대로(위 → 아래) 카운트업하고 마지막에 stamps 중 Show인 것을 동시에 찍는다.
        /// 도장 스프라이트는 호출 전에 바꿔 둘 것. 이 컴포넌트는 크기·알파만 움직인다.
        /// 도장은 Show 여부와 상관없이 매번 전부 넘길 것 (정지 포즈를 캡처하고, 안 찍는 도장을 숨기는 데 필요하다).
        /// </summary>
        public void Play(IReadOnlyList<Row> rows, IReadOnlyList<Stamp> stamps)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugLastRows = rows;
            debugLastStamps = stamps;
#endif

            // 이전 연출의 잔상을 지우고 정지 포즈로 되돌린다
            KillSequence();
            ResetToRest();
            CaptureRest(rows, stamps);

            // 도장은 전부 숨긴 상태(크게, 투명)에서 시작한다. Show가 false인 도장은 끝까지 이 상태로 남는다
            for (int i = 0; i < stamps.Count; i++)
            {
                Image image = stamps[i].Image;
                StampRest rest = stampRests[image];
                image.rectTransform.localScale = rest.Scale * stampStartScale;
                Color hidden = rest.Color;
                hidden.a = 0f;
                image.color = hidden;
            }

            sequence = DOTween.Sequence().SetLink(gameObject);
            float lastEnd = 0f;
            float groupStart = 0f;   // 현재 그룹 첫 줄의 시작 시각
            int indexInGroup = 0;    // 그룹 안에서 몇 번째 줄인지 (rowStagger 간격 계산용)

            for (int i = 0; i < rows.Count; i++)
            {
                // 새 그룹은 앞 줄들이 모두 끝난 뒤 groupGap만큼 쉬고 시작한다 (첫 줄은 그룹 표시가 있어도 바로 시작)
                if (rows[i].StartsGroup && i > 0)
                {
                    groupStart = lastEnd + groupGap;
                    indexInGroup = 0;
                }

                float startTime = groupStart + indexInGroup * rowStagger;
                lastEnd = Mathf.Max(lastEnd, AddRow(startTime, rows[i]));
                indexInGroup++;
            }

            // 도장 표시: 크게 나타났다가 원래 크기로 내리꽂힌다. 페이드는 절반 시간에 끝내 착지 전에 또렷하게 보이게 한다
            // Show인 도장은 모두 같은 시각에 동시에 찍는다
            float stampAt = lastEnd + stampDelay;
            bool anyStamp = false;
            for (int i = 0; i < stamps.Count; i++)
            {
                if (!stamps[i].Show) continue;

                Image image = stamps[i].Image;
                StampRest rest = stampRests[image];
                sequence.Insert(stampAt, image.rectTransform.DOScale(rest.Scale, stampDuration).SetEase(stampEase));
                sequence.Insert(stampAt, image.DOFade(rest.Color.a, stampDuration * 0.5f));
                anyStamp = true;
            }

            // 도장 효과음은 내리꽂혀 착지하는 순간('쾅')에 한 번. 시퀀스 안에 넣어 도중에 Kill되면 울리지 않게 한다
            if (anyStamp)
                sequence.InsertCallback(stampAt + stampDuration, PlayStamp);
        }

        // 한 줄의 카운트업과 완료 펀치를 시퀀스에 넣고, 그 줄이 끝나는 시각을 반환한다
        private float AddRow(float startTime, Row row)
        {
            float duration = rowDuration;
            if (row.IsFinale)
            {
                duration = finaleDuration;
            }

            float endTime = startTime + duration;

            sequence.Insert(startTime, CountTo(row.Text, row.Target, duration, row.Format));
            // 펀치도 같은 시퀀스에 넣어 Kill 한 번으로 함께 정리되게 한다
            sequence.Insert(endTime, row.Text.rectTransform.DOPunchScale(Vector3.one * punchScale, punchDuration, 1, 0f));
            return endTime;
        }

        // 0 → target으로 값을 올리며 텍스트를 갱신한다. 표시 문자열이 바뀔 때만 갱신하고 틱을 울린다
        private Tweener CountTo(TMP_Text text, float target, float duration, Func<float, string> format)
        {
            float value = 0f;
            string shown = format(0f);
            text.text = shown;

            return DOTween.To(() => value, v =>
            {
                value = v;
                string next = format(v);
                if (next == shown) return;

                shown = next;
                text.text = next;
                PlayTick();
            }, target, duration).SetEase(countEase);
        }

        private void PlayTick()
        {
            if (oneShots == null) return;

            float now = Time.unscaledTime;
            if (now - lastTickTime < tickMinInterval) return;

            lastTickTime = now;
            oneShots.Play(tickSound);
        }

        private void PlayStamp()
        {
            if (oneShots == null) return;

            oneShots.Play(stampSound);
        }

        private void KillSequence()
        {
            if (sequence != null && sequence.IsActive())
            {
                sequence.Kill();
            }
            sequence = null;
        }

        // 처음 보는 텍스트·도장의 정지 포즈를 기록한다. ResetToRest 직후라 이미 아는 것은 정지 포즈 상태다
        private void CaptureRest(IReadOnlyList<Row> rows, IReadOnlyList<Stamp> stamps)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RectTransform rect = rows[i].Text.rectTransform;
                if (!textRestScales.ContainsKey(rect))
                {
                    textRestScales.Add(rect, rect.localScale);
                }
            }

            for (int i = 0; i < stamps.Count; i++)
            {
                Image image = stamps[i].Image;
                if (!stampRests.ContainsKey(image))
                {
                    stampRests.Add(image, new StampRest { Scale = image.rectTransform.localScale, Color = image.color });
                }
            }
        }

        // 펀치 도중 끊긴 텍스트 스케일과 도장을 정지 포즈로 되돌린다
        private void ResetToRest()
        {
            foreach (KeyValuePair<RectTransform, Vector3> pair in textRestScales)
            {
                pair.Key.localScale = pair.Value;
            }

            foreach (KeyValuePair<Image, StampRest> pair in stampRests)
            {
                pair.Key.rectTransform.localScale = pair.Value.Scale;
                pair.Key.color = pair.Value.Color;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ─── 개발용: 수치를 조정한 뒤 곡을 다시 플레이하지 않고 연출만 다시 본다 (정식 빌드에서는 제외) ───
        // 주의: 플레이 중 인스펙터에서 바꾼 값은 플레이를 멈추면 사라진다.
        //       Copy Component → 정지 후 ResultUI 프리팹의 ResultCountAnimator에 Paste Component Values로 옮길 것
        private IReadOnlyList<Row> debugLastRows;
        private IReadOnlyList<Stamp> debugLastStamps;

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f5Key.wasPressedThisFrame)
            {
                if (keyboard.shiftKey.isPressed)
                {
                    DebugReplayAllStamps();
                }
                else
                {
                    DebugReplay();
                }
            }
        }

        [Sirenix.OdinInspector.Button("연출 다시 재생 (F5)"), Sirenix.OdinInspector.DisableInEditorMode]
        private void DebugReplay()
        {
            if (!HasDebugReplayData()) return;

            Play(debugLastRows, debugLastStamps);
        }

        // 신기록이 아니었던 판에서도 NEW RECORD 도장 연출을 조정할 수 있게 모든 도장을 찍는다
        [Sirenix.OdinInspector.Button("연출 다시 재생 - 도장 전부 표시 (Shift+F5)"), Sirenix.OdinInspector.DisableInEditorMode]
        private void DebugReplayAllStamps()
        {
            if (!HasDebugReplayData()) return;

            var allStamps = new Stamp[debugLastStamps.Count];
            for (int i = 0; i < debugLastStamps.Count; i++)
            {
                allStamps[i] = new Stamp(debugLastStamps[i].Image, true);
            }

            // Play가 debugLastStamps를 덮으므로 되돌려 둔다. 이후 F5는 실제 결과(신기록 여부 그대로)로 재생된다
            IReadOnlyList<Stamp> actualStamps = debugLastStamps;
            Play(debugLastRows, allStamps);
            debugLastStamps = actualStamps;
        }

        private bool HasDebugReplayData()
        {
            if (debugLastRows == null || debugLastStamps == null)
            {
                Debug.LogWarning("[ResultCountAnimator] 다시 재생할 결과 데이터가 없습니다. 곡을 한 번 플레이해 결과 화면을 띄운 뒤 사용하세요.");
                return false;
            }
            return true;
        }
#endif
    }
}
