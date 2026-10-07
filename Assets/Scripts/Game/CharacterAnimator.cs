using System;
using DG.Tweening;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Boot;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using SCOdyssey.Rhythm;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 흐름 ────────────────────────────────────────────────────────────────
    //
    //  판정 버스는 한 번의 키 입력에 대해 LaneInput과 NoteJudged를 한 프레임 안에 줄줄이 발화한다.
    //  받는 족족 애니메이션을 바꾸면 나중에 온 이벤트가 앞선 것을 덮어쓴다.
    //  그래서 핸들러는 버퍼에 기록만 하고, 해석은 LateUpdate에서 한 번만 한다.
    //
    //    버스 이벤트 2종 → _frame에 적재 → (시간창 대기) → Resolve → TweenY + Play
    //
    //  Resolve는 static이라 인스턴스 필드에 손댈 수 없다. 전이 규칙이 전부 그 안에만 있다는 뜻이다.
    //  부수효과는 TweenY와 Play 둘뿐이다.
    //
    //  판정선(TimelineController)의 자식이라 X 이동은 부모를 따라 자동으로 되고,
    //  여기서는 _spriteRoot의 Y만 제어한다.
    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// 캐릭터 상태 머신. 판정 버스의 입력·판정 이벤트를 자기 그룹만 받아 Y 위치와 애니메이션을 결정한다.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder.Early)]   // Spine의 LateUpdate보다 먼저 돌아야 같은 프레임에 반영된다
    public class CharacterAnimator : MonoBehaviour
    {
        // ※ 아래 네 필드는 이름을 바꾸면 Timeline.prefab에 직렬화된 값과 참조가 끊긴다.
        [Header("References")]
        [SerializeField] private GameObject _spriteRoot;

        [Header("Y Positions")]
        [SerializeField] private float _topY = 120f;
        [SerializeField] private float _bottomY = -120f;
        [SerializeField] private float _centerY = 0f;

        [Header("Y Move")]
        [SerializeField, Range(0.02f, 0.5f)] private float _moveDuration = 0.12f;
        [SerializeField] private Ease _moveEase = Ease.OutQuad;

        [Header("Animation")]
        [Tooltip("상태 전환 블렌딩 시간. 길면 타격감이 뭉개진다.")]
        [SerializeField, Range(0f, 0.3f)] private float _animMixDuration = 0.05f;

        [Header("Input")]
        [Tooltip("첫 입력 후 반대편을 기다리는 시간. 키울수록 상하 동시 인식이 관대해지고 반응이 늦어진다. 0이면 즉시 해석.")]
        [SerializeField, Range(0f, 0.1f)] private float _pairWindow = 0.02f;

        [Header("Debug")]
        [SerializeField] private bool _verboseLog;

        private const int VariantCount = 3;   // Hit_master_1~3 / Hit_1~3

        [Flags]
        private enum HoldMask
        {
            None   = 0,
            Top    = 1,
            Bottom = 2,
            Both   = Top | Bottom,
        }

        /// 이번 주기에 캐릭터가 할 일의 종류. StateTable의 행 순서와 일치해야 한다.
        private enum ActionKind
        {
            Idle = 0,
            Hit = 1,
            HitWhileHold = 2,
            HoldEnter = 3,
            Whiff = 4,
            DoubleHit = 5,
        }

        /// 애니메이션 이름 접두사를 고르는 축. StateTable의 열 순서와 일치해야 한다.
        /// 이동이 있으면 진행 방향이고, HitWhileHold에서는 "누른 쪽"을 뜻할 뿐 이동이 아니다.
        private enum Axis
        {
            Up = 0,
            Down = 1,
            None = 2,
        }

        private struct SidePress
        {
            public bool Pressed;
            public bool Whiff;        // 칠 노트가 없는데 누름
            public JudgeType Judge;   // Whiff이면 의미 없음
            public double Time;       // 입력 시각. 반대편과 얼마나 붙어 있었는지 판단에 쓴다
        }

        /// 한 해석 주기 동안 모인 원시 입력. 해석하면서 비운다.
        private struct FrameInput
        {
            public SidePress Top;
            public SidePress Bottom;
            public HoldMask HoldBegan;
            public HoldMask HoldStopped;

            public bool AnyPress
            {
                get { return Top.Pressed || Bottom.Pressed; }
            }

            public bool IsEmpty
            {
                get
                {
                    return !AnyPress
                        && HoldBegan == HoldMask.None
                        && HoldStopped == HoldMask.None;
                }
            }
        }

        private struct Resolution
        {
            public NotePosition Pos;
            public CharacterState State;
            public int Variant;
        }

        // 표 C. 행 = ActionKind, 열 = Axis(Up, Down, None).
        // ★ 행/열 순서가 위 enum 순서와 어긋나면 조용히 틀린 애니메이션이 나간다.
        // NA 칸은 ResolveState의 가드 두 개가 먼저 걸러내므로 도달하지 않는다.
        private const CharacterState NA = CharacterState.Run;

        private static readonly CharacterState[,] StateTable =
        {
            /* Idle         */ { NA,                               NA,                                 NA },
            /* Hit          */ { CharacterState.Up_hit,            CharacterState.Down_hit,            NA },
            /* HitWhileHold */ { CharacterState.Up_hit_while_hold, CharacterState.Down_hit_while_hold, NA },
            /* HoldEnter    */ { CharacterState.Up_hold,           CharacterState.Down_hold,           CharacterState.Hold },
            /* Whiff        */ { CharacterState.Miss,              CharacterState.Miss,                CharacterState.Miss },
            /* DoubleHit    */ { CharacterState.Double_hit,        CharacterState.Double_hit,          CharacterState.Double_hit },
        };

        private LaneGroup _group;
        private HoldMask _holds;
        private NotePosition _pos = NotePosition.Bottom;
        private CharacterState _current = (CharacterState)(-1);   // 아직 아무것도 재생하지 않았음
        private int _lastVariant = -1;

        // 잡고 있는 홀드의 꼬리 NoteId. 없으면 -1. 꼬리 판정은 이 id일 때만 홀드 종료로 본다
        private int _topTail = -1;
        private int _bottomTail = -1;

        private FrameInput _frame;
        private float _waitUntil;

        private ICharacterAnimationHandler _handler;
        private IJudgementBus _bus;
        private IJudgeStateReader _reader;
        private RectTransform _spriteRect;
        private Tween _yTween;

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Awake()
        {
            if (_spriteRoot == null)
            {
                Debug.LogError("[CharacterAnimator] _spriteRoot가 비어 있습니다. 캐릭터가 아무것도 하지 않습니다.", this);
                return;
            }

            _spriteRect = _spriteRoot.transform as RectTransform;
            if (_spriteRect == null)
            {
                Debug.LogError("[CharacterAnimator] _spriteRoot가 RectTransform이 아닙니다. Y 이동이 동작하지 않습니다.", this);
            }
        }

        private void Start()
        {
            if (ServiceLocator.TryGet<ICharacterManager>(out var characterManager))
            {
                CharacterSO skin = characterManager.GetCurrentSkin();
                if (skin != null)
                {
                    LoadCharacter(skin);
                }
            }

            // 구독을 OnEnable이 아니라 Start에 두는 이유: OnEnable은 GameManager가 버스를
            // ServiceLocator에 등록하기 전에 돌 수 있다(풀 생성 시점이 Init보다 앞선다).
            // 버스가 없는 씬(채보 에디터 프리뷰)에서는 구독하지 않고 Run만 돈다. 에러가 아니다.
            if (ServiceLocator.TryGet<IJudgementBus>(out _bus))
            {
                _bus.LaneInput += OnLaneInput;
                _bus.NoteJudged += OnNoteJudged;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.LaneInput -= OnLaneInput;
                _bus.NoteJudged -= OnNoteJudged;
            }
        }

        private void OnEnable()
        {
            // 판정선은 풀에서 돌려쓴다. 반환 시 SetActive(false)만 하므로 이 컴포넌트는 살아 있다.
            // 비활성 중 이벤트는 isActiveAndEnabled 가드가 버리지만, 이전 마디의 홀드 상태가
            // 새 판정선에서 부활하지 않도록 여기서 전부 리셋한다.
            _holds = HoldMask.None;
            _pos = NotePosition.Bottom;
            _frame = default;
            _lastVariant = -1;
            _topTail = -1;
            _bottomTail = -1;

            // Spine 트랙은 비활성 동안 얼어붙어 있다가 재활성 시 그 자리에서 이어 재생된다.
            // _current를 비우지 않으면 아래 Play(Run)이 루프 가드에 걸려 조기 반환하고,
            // 이전 판정선이 재생 중이던 원샷의 뒷부분이 새 캐릭터에서 이어 나온다.
            _current = (CharacterState)(-1);

            SnapY(_bottomY);
            Play(CharacterState.Run);
        }

        private void OnDisable()
        {
            // 씬 언로드 때도 OnDisable이 먼저 오므로 트윈 정리는 여기 한 곳이면 된다.
            if (_yTween != null)
            {
                _yTween.Kill();
                _yTween = null;
            }
        }

        // ─────────────────────────────────────────────
        // Public API  (TimelineController가 호출)
        // ─────────────────────────────────────────────

        public void SetGroup(LaneGroup group)
        {
            _group = group;
            SyncHoldsFromEngine();
        }

        public void LoadCharacter(CharacterSO so)
        {
            if (so is SpriteSheetCharacterSO)
            {
                _handler = new SpriteSheetAnimationHandler();
            }
            else
            {
                _handler = new SpineAnimationHandler();
            }

            _handler.Initialize(_spriteRoot, so, _animMixDuration);

            // 핸들러가 방금 준비됐으므로 추적값을 비우고 기본 상태를 강제로 재생한다.
            // SetGroup 시딩이 먼저 돌았으면 홀드 중이므로 Run이 아니라 Hold다.
            _current = (CharacterState)(-1);
            Play(FollowOf(_holds));
        }

        // ─────────────────────────────────────────────
        // 버스 수신 — 레인을 그룹·위치로 바꾸고 자기 그룹만 기록한다
        // ─────────────────────────────────────────────

        private void OnLaneInput(LaneKeyEvent e)
        {
            if (!isActiveAndEnabled) return;
            if (LaneLayout.GroupOf(e.Lane) != _group) return;

            NotePosition pos = LaneLayout.PositionOf(e.Lane);
            if (e.IsPressed)
            {
                RecordPress(pos, e.Time);
            }
            else
            {
                StopHold(pos);
            }
        }

        // 누름의 적중은 같은 Press 호출 안에서 LaneInput 바로 뒤에 동기로 온다.
        // 그래서 버퍼에 같은 시각의 누름이 있으면 그 누름이 친 것이고, 끝까지 안 오면 헛침이다.
        private void OnNoteJudged(JudgeEvent e)
        {
            if (!isActiveAndEnabled) return;
            if (LaneLayout.GroupOf(e.Lane) != _group) return;

            NotePosition pos = LaneLayout.PositionOf(e.Lane);

            if (e.Kind == NoteKind.HoldTail)
            {
                // 꼬리는 적중이든 miss든 홀드의 끝이다. 단, 잡고 있는 그 꼬리일 때만 —
                // 떼지 않은 채 다음 머리를 치면 새 머리의 적중이 옛 꼬리의 miss보다 먼저 와서 id가 이미 바뀌어 있다.
                if (e.NoteId == TailOf(pos))
                {
                    StopHold(pos);
                }
                return;
            }

            // 지나쳐서 놓친 탭·머리에는 반응하지 않는다. 그 경로에는 입력이 없다.
            if (e.IsMiss) return;

            MarkHit(pos, e.Judge, e.Time);
            if (e.Kind == NoteKind.HoldHead)
            {
                BeginHold(pos, e.PairId);
            }
        }

        // ─────────────────────────────────────────────
        // 기록 — 판단은 하지 않는다
        // ─────────────────────────────────────────────

        private void RecordPress(NotePosition pos, double time)
        {
            bool isTop = pos == NotePosition.Top;

            // 버퍼는 레인당 한 칸뿐이다. 그냥 덮어쓰면 앞 타격의 반응이 통째로 사라진다.
            // "핸들러는 기록만 한다"는 원칙의 유일한 예외 — 지금 비우지 않으면 정보가 없어진다.
            if (MustFlushBefore(time, isTop))
            {
                ResolveAndApply();
            }

            bool hadPress = _frame.AnyPress;

            // 등급은 아직 모른다. 헛침으로 적어 두고, 적중이 뒤따르면 MarkHit이 고친다.
            SidePress press;
            press.Pressed = true;
            press.Whiff = true;
            press.Judge = JudgeType.Umm;
            press.Time = time;

            if (isTop)
            {
                _frame.Top = press;
            }
            else
            {
                _frame.Bottom = press;
            }

            // 창을 여는 조건은 "버퍼가 비었을 때"가 아니라 "직전에 press가 없었을 때"다.
            // 홀드 해제가 먼저 기록되면 버퍼는 비어있지 않아서, 전자로 하면 창이 열리지 않는다.
            if (!hadPress)
            {
                _waitUntil = Time.unscaledTime + _pairWindow;
            }
        }

        /// <summary>
        /// 이 입력을 기록하기 전에 기존 버퍼를 먼저 해석해야 하는가.
        /// 같은 레인 재입력이면 덮어쓰기가 되고, 반대편이라도 시각이 멀면 동시 입력이 아니다.
        /// 둘 다 앞선 입력을 하나의 독립된 타격으로 살려내야 하는 경우다.
        /// </summary>
        private bool MustFlushBefore(double time, bool isTop)
        {
            SidePress same;
            SidePress other;
            if (isTop)
            {
                same = _frame.Top;
                other = _frame.Bottom;
            }
            else
            {
                same = _frame.Bottom;
                other = _frame.Top;
            }

            if (same.Pressed) return true;

            if (other.Pressed && Math.Abs(time - other.Time) > _pairWindow) return true;

            return false;
        }

        // 방금 기록한 누름이 친 노트. 같은 시각의 누름이 버퍼에 있을 때만 헛침 표시를 거둔다.
        private void MarkHit(NotePosition pos, JudgeType judge, double time)
        {
            ref SidePress press = ref SideOf(pos);
            if (!press.Pressed || press.Time != time) return;

            press.Whiff = false;
            press.Judge = judge;
        }

        private ref SidePress SideOf(NotePosition pos)
        {
            if (pos == NotePosition.Top) return ref _frame.Top;
            return ref _frame.Bottom;
        }

        private void BeginHold(NotePosition pos, int tailId)
        {
            HoldMask bit = MaskOf(pos);
            if (bit == HoldMask.None) return;

            // 비트가 이미 서 있어도 id는 간다. 떼지 않은 채 다음 머리를 친 경우라 옛 꼬리의 miss가 뒤따르는데,
            // 그 miss가 새 홀드를 끊지 않으려면 지금 갈아 둬야 한다.
            SetTail(pos, tailId);

            // 시딩과 적중이 겹쳐 와도 이미 잡고 있으면 아무 일도 없어야 한다.
            if ((_holds & bit) != 0) return;

            _holds |= bit;
            _frame.HoldBegan |= bit;
        }

        /// <summary>
        /// 홀드가 끝났다. 원인은 셋(키 뗌 / 꼬리 판정 / 꼬리 miss)인데 여기서는 구분하지 않는다.
        /// 어느 쪽이든 하는 일은 비트를 내리는 것뿐이다.
        /// </summary>
        private void StopHold(NotePosition pos)
        {
            HoldMask bit = MaskOf(pos);
            if (bit == HoldMask.None) return;

            SetTail(pos, -1);

            // 일반 노트를 톡 치고 손을 떼도 여기로 온다. 잡고 있지 않았으면 아무 일도 없어야 한다.
            // 이 가드가 없으면 모든 탭 직후 Run이 히트 원샷을 잘라먹는다.
            // 같은 홀드에 대해 키 뗌과 꼬리 판정이 겹쳐 와도 두 번째부터는 여기서 걸린다.
            if ((_holds & bit) == 0) return;

            _holds &= ~bit;
            _frame.HoldStopped |= bit;
        }

        private int TailOf(NotePosition pos)
        {
            return pos == NotePosition.Top ? _topTail : _bottomTail;
        }

        private void SetTail(NotePosition pos, int tailId)
        {
            if (pos == NotePosition.Top)
            {
                _topTail = tailId;
            }
            else
            {
                _bottomTail = tailId;
            }
        }

        // 판정선은 한 마디 앞서 생기므로, 두 마디 넘게 이어지는 홀드는 머리 적중을 못 본 캐릭터가 생긴다.
        // 엔진이 잡고 있는 꼬리로 메운다. 게임 밖(프리뷰)에는 리더가 없으므로 아무 일도 없다.
        private void SyncHoldsFromEngine()
        {
            if (!ServiceLocator.TryGet<IJudgeStateReader>(out _reader)) return;

            for (int i = 0; i < LANE_COUNT; i++)
            {
                Lane lane = (Lane)i;
                if (LaneLayout.GroupOf(lane) != _group) continue;

                int tail = _reader.HoldInProgressOf(lane);
                if (tail >= 0)
                {
                    BeginHold(LaneLayout.PositionOf(lane), tail);
                }
            }
        }

        // ─────────────────────────────────────────────
        // 해석 시점
        // ─────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_frame.IsEmpty) return;
            if (!ShouldResolveNow()) return;

            ResolveAndApply();
        }

        private bool ShouldResolveNow()
        {
            // 홀드 변화만 있으면 짝지을 상대가 없다. 기다리면 홀드 해제 반응만 굼떠진다.
            if (!_frame.AnyPress) return true;

            // 양쪽이 다 왔으면 더 기다릴 이유가 없다.
            if (_frame.Top.Pressed && _frame.Bottom.Pressed) return true;

            return Time.unscaledTime >= _waitUntil;
        }

        private void ResolveAndApply()
        {
            FrameInput input = _frame;
            _frame = default;

            Resolution resolution = Resolve(input, _holds, _pos, _lastVariant);
            _lastVariant = resolution.Variant;

            if (resolution.Pos != _pos)
            {
                _pos = resolution.Pos;
                TweenY(YOf(_pos));
            }

            Play(resolution.State);

            LogResolution(input, resolution);
        }

        // ─────────────────────────────────────────────
        // 전이 규칙 — 전부 여기 안에만 있다 (static이라 인스턴스 필드에 손댈 수 없음)
        // ─────────────────────────────────────────────

        private static Resolution Resolve(FrameInput input, HoldMask holds, NotePosition prev, int lastVariant)
        {
            FrameInput filtered = DropWhiffs(input, holds);
            HoldMask pressed = PressedMask(filtered);

            // 홀드가 걸리지 않은 쪽에서 들어온 진짜 히트.
            // 홀드 진입 프레스 자신은 그 쪽 비트가 이미 holds에 서 있으므로 여기서 빠진다.
            // holds가 Both면 항상 None이라, 상하 동시 홀드 진입은 A2로 새지 않는다.
            HoldMask hitSides = RealHitMask(filtered) & ~holds;

            NotePosition pos = ResolvePosition(holds, pressed, prev);
            bool moved = pos != prev;

            ActionKind action = ResolveAction(filtered, holds, hitSides, moved);
            Axis axis = ResolveAxis(action, hitSides, prev, pos, moved);

            int variant;
            CharacterState state = ResolveState(action, axis, filtered, holds, lastVariant, out variant);

            Resolution resolution;
            resolution.Pos = pos;
            resolution.State = state;
            resolution.Variant = variant;
            return resolution;
        }

        /// <summary>
        /// 전처리. 표를 보기 전에 무시해야 할 헛침을 걷어낸다.
        ///  F1 — 홀드 중이면 헛침은 아무 일도 일으키지 않는다. Y도 안 움직이고 Miss도 안 낸다.
        ///  F2 — 같은 주기에 진짜 히트가 있으면 헛침은 없었던 것으로 친다.
        ///       위에만 노트가 있는데 상하를 동시에 누른 경우가 여기 해당한다.
        /// </summary>
        private static FrameInput DropWhiffs(FrameInput input, HoldMask holds)
        {
            bool hasRealHit = (input.Top.Pressed && !input.Top.Whiff)
                           || (input.Bottom.Pressed && !input.Bottom.Whiff);

            if (holds == HoldMask.None && !hasRealHit) return input;

            if (input.Top.Pressed && input.Top.Whiff)
            {
                input.Top = default;
            }
            if (input.Bottom.Pressed && input.Bottom.Whiff)
            {
                input.Bottom = default;
            }
            return input;
        }

        private static HoldMask PressedMask(FrameInput input)
        {
            HoldMask mask = HoldMask.None;
            if (input.Top.Pressed)
            {
                mask |= HoldMask.Top;
            }
            if (input.Bottom.Pressed)
            {
                mask |= HoldMask.Bottom;
            }
            return mask;
        }

        /// 헛침을 걷어낸 뒤 남은 "진짜 히트"의 비트마스크.
        private static HoldMask RealHitMask(FrameInput input)
        {
            HoldMask mask = HoldMask.None;
            if (input.Top.Pressed && !input.Top.Whiff)
            {
                mask |= HoldMask.Top;
            }
            if (input.Bottom.Pressed && !input.Bottom.Whiff)
            {
                mask |= HoldMask.Bottom;
            }
            return mask;
        }

        /// <summary>
        /// 표 A. 홀드가 입력보다 항상 우선이다 — 그래서 홀드 중 반대편 입력은 Y를 흔들지 않는다.
        /// 헛침은 이 표에 등장하지 않으므로 "헛침도 Y는 이동" 규칙이 특수 분기 없이 성립한다.
        /// </summary>
        private static NotePosition ResolvePosition(HoldMask holds, HoldMask pressed, NotePosition prev)
        {
            if (holds != HoldMask.None) return PositionOf(holds);
            if (pressed != HoldMask.None) return PositionOf(pressed);
            return prev;
        }

        /// 홀드 조합과 입력 조합이 공유하는 매핑. 상하 동시면 가운데다.
        private static NotePosition PositionOf(HoldMask mask)
        {
            if (mask == HoldMask.Both) return NotePosition.Middle;
            if (mask == HoldMask.Top) return NotePosition.Top;
            return NotePosition.Bottom;
        }

        /// <summary>
        /// 표 B. 위에서부터 첫 일치. 우선순위가 있는 조건이라 표로 접지 않는다.
        /// hitSides는 "홀드가 걸리지 않은 쪽에서 들어온 진짜 히트"다.
        /// </summary>
        private static ActionKind ResolveAction(FrameInput input, HoldMask holds, HoldMask hitSides, bool moved)
        {
            bool topHit = input.Top.Pressed && !input.Top.Whiff;
            bool bottomHit = input.Bottom.Pressed && !input.Bottom.Whiff;

            // A1 — 둘 다 진짜 히트여야 한다. 한쪽이 헛침이면 F2가 이미 걷어냈다.
            if (topHit && bottomHit && holds == HoldMask.None) return ActionKind.DoubleHit;

            // A2 — 한쪽을 잡은 채 비어 있는 쪽을 쳤다.
            //      홀드 진입 프레스와 반대편 일반 노트가 같은 주기에 겹쳐도 여기서 잡힌다.
            //      예전에는 아래 A3이 먼저 걸려 그 경우 반대편 타격이 통째로 사라졌다.
            //      이동 중이어도 히트가 우선이다 — 이동은 트윈이 표현하고 클립은 타격을 표현한다.
            //
            //      "반대편"을 따로 검사하지 않는 이유: hitSides가 이미 holds를 빼고 남은 것이라
            //      잡고 있는 쪽의 히트는 들어올 수 없다. holds가 Both면 항상 비어 발화하지 않는데,
            //      그 조합에서 진짜 히트가 가능하려면 이번 주기에 시작한 홀드여야 하고
            //      그러면 HoldBegan이 서서 아래 A3이 잡는다.
            //      (잡고 있는 쪽을 다시 누르는 입력은 없다 — 키를 떼는 순간 엔진이 꼬리를 끊고 비트가 내려간다)
            if (holds != HoldMask.None && hitSides != HoldMask.None) return ActionKind.HitWhileHold;

            // A3 — 홀드에 새로 들어갔거나, 남은 홀드 쪽으로 자리를 옮겼다
            if (holds != HoldMask.None && (input.HoldBegan != HoldMask.None || moved)) return ActionKind.HoldEnter;

            // A4 — 필터를 통과해 남은 헛침이면 홀드도 없고 진짜 히트도 없다는 뜻이다
            bool whiffRemains = (input.Top.Pressed && input.Top.Whiff)
                             || (input.Bottom.Pressed && input.Bottom.Whiff);
            if (whiffRemains) return ActionKind.Whiff;

            // A5
            if (topHit || bottomHit) return ActionKind.Hit;

            // A6 — 홀드를 놓았거나 아무 일도 없었다
            return ActionKind.Idle;
        }

        /// <summary>
        /// 이동이 동반되면 방향 애니메이션이 등급보다 우선한다는 규칙이 오직 여기에만 있다.
        /// 축이 None이 아닌 순간 등급 애니메이션은 표에서 도달 불가능해진다.
        /// </summary>
        private static Axis ResolveAxis(ActionKind action, HoldMask hitSides, NotePosition prev, NotePosition pos, bool moved)
        {
            // 홀드 중 비어 있는 쪽을 쳤다. 여기서 축은 "친 레인"을 가리켜 클립 이름 접두사를 고를 뿐이고
            // 이동이 아니다. 홀드 진입과 겹쳐 Y가 움직이는 중에도 히트가 우선이라 이 분기가 위에 있다.
            // 그 경우 접두사가 이동 방향과 반대가 되는데, 이동은 트윈(0.12초)이 이미 표현하고 있다.
            //
            // ★ 누른 쪽(Pressed)을 보면 안 된다. 홀드 진입과 겹치면 홀드 쪽도 Pressed라 접두사가 뒤집힌다.
            //   hitSides는 holds를 뺀 것이라 이 분기에서는 항상 한쪽 비트만 서 있다.
            if (action == ActionKind.HitWhileHold)
            {
                if ((hitSides & HoldMask.Top) != 0) return Axis.Up;
                return Axis.Down;
            }

            if (moved)
            {
                if (HeightOf(pos) > HeightOf(prev)) return Axis.Up;
                return Axis.Down;
            }

            return Axis.None;
        }

        private static int HeightOf(NotePosition pos)
        {
            if (pos == NotePosition.Top) return 2;
            if (pos == NotePosition.Middle) return 1;
            return 0;
        }

        /// <summary>
        /// 표 C. 표에 담을 수 없는 두 경우를 먼저 걸러내고 나머지는 조회한다.
        /// </summary>
        private static CharacterState ResolveState(ActionKind action, Axis axis, FrameInput input,
                                                   HoldMask holds, int lastVariant, out int variant)
        {
            variant = lastVariant;

            // 홀드 여부에 따라 달라지므로 표에 상수로 담을 수 없다
            if (action == ActionKind.Idle) return FollowOf(holds);

            // 판정 등급과 랜덤 변형이 필요하므로 표에 담을 수 없다
            if (action == ActionKind.Hit && axis == Axis.None)
            {
                return GradeState(JudgeOf(input), lastVariant, out variant);
            }

            return StateTable[(int)action, (int)axis];
        }

        /// <summary>
        /// 표 D. 제자리 히트에서만 도달한다. 이동이 있으면 축이 방향을 갖기 때문이다.
        /// </summary>
        private static CharacterState GradeState(JudgeType judge, int lastVariant, out int variant)
        {
            variant = lastVariant;

            if (judge == JudgeType.Umm) return CharacterState.Hit_umm;

            variant = PickVariant(lastVariant);

            if (judge == JudgeType.Perfect || judge == JudgeType.Master)
            {
                return (CharacterState)((int)CharacterState.Hit_master_1 + variant);
            }
            return (CharacterState)((int)CharacterState.Hit_1 + variant);
        }

        /// 직전과 다른 변형을 고른다. 같은 클립이 연속으로 나오면 반복이 눈에 띈다.
        private static int PickVariant(int lastVariant)
        {
            int pick = UnityEngine.Random.Range(0, VariantCount);
            while (pick == lastVariant)
            {
                pick = UnityEngine.Random.Range(0, VariantCount);
            }
            return pick;
        }

        private static JudgeType JudgeOf(FrameInput input)
        {
            if (input.Top.Pressed && !input.Top.Whiff) return input.Top.Judge;
            return input.Bottom.Judge;
        }

        /// 원샷이 끝나면 돌아갈 루프. 그 순간의 실제 홀드 상태에서 계산되므로 틀릴 수 없다.
        private static CharacterState FollowOf(HoldMask holds)
        {
            if (holds == HoldMask.None) return CharacterState.Run;
            return CharacterState.Hold;
        }

        private static HoldMask MaskOf(NotePosition pos)
        {
            if (pos == NotePosition.Top) return HoldMask.Top;
            if (pos == NotePosition.Bottom) return HoldMask.Bottom;
            return HoldMask.None;   // LaneLayout은 Middle을 내지 않는다. 방어용.
        }

        // ─────────────────────────────────────────────
        // 적용 — 부수효과는 여기 둘뿐
        // ─────────────────────────────────────────────

        private void Play(CharacterState state)
        {
            if (_handler == null) return;

            CharacterState follow = FollowOf(_holds);
            bool isLoop = CharacterStateInfo.IsLoop(state);

            // 이미 같은 루프를 돌고 있으면 처음부터 다시 시작시키지 않는다
            if (isLoop && state == _current) return;

            _handler.Play(state, follow);

            // ★ _current에는 "요청한 상태"가 아니라 "화면에 남을 상태"를 기록한다.
            //   원샷은 핸들러가 뒤에 follow를 이어붙이므로 끝나면 follow가 남는다.
            //   요청값을 그대로 넣으면 위의 루프 재시작 방지가 동작하지 않는다.
            if (isLoop)
            {
                _current = state;
            }
            else
            {
                _current = follow;
            }
        }

        private void TweenY(float y)
        {
            if (_spriteRect == null) return;

            if (_yTween != null)
            {
                _yTween.Kill();
            }

            // SetRecyclable(false)를 명시하는 이유: 재활용이 켜지면 자동 kill된 트윈이 풀로 돌아가
            // 다른 트윈으로 재사용되고, _yTween 핸들이 남의 트윈을 가리켜 엉뚱한 것을 죽이게 된다.
            // 지금은 프로젝트 설정이 꺼져 있지만 누가 바꿔도 이 코드는 영향받지 않아야 한다.
            _yTween = _spriteRect.DOAnchorPosY(y, _moveDuration)
                                 .SetEase(_moveEase)
                                 .SetRecyclable(false);
        }

        private void SnapY(float y)
        {
            if (_spriteRect == null) return;

            Vector2 position = _spriteRect.anchoredPosition;
            position.y = y;
            _spriteRect.anchoredPosition = position;
        }

        private float YOf(NotePosition pos)
        {
            if (pos == NotePosition.Top) return _topY;
            if (pos == NotePosition.Middle) return _centerY;
            return _bottomY;
        }

        // ─────────────────────────────────────────────
        // 진단
        // ─────────────────────────────────────────────

        /// <summary>
        /// 해석이 일어난 주기에만 한 줄 남긴다. 이벤트마다 찍으면 문자열 보간 비용이 입력 경로에 붙는다.
        /// Conditional은 호출뿐 아니라 인자 평가까지 컴파일 단계에서 지우므로 릴리즈에서는 비용이 0이다.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogResolution(FrameInput input, Resolution resolution)
        {
            if (!_verboseLog) return;

            Debug.Log($"[CA {_group}] press(T={input.Top.Pressed}/whiff={input.Top.Whiff}, " +
                      $"B={input.Bottom.Pressed}/whiff={input.Bottom.Whiff}) " +
                      $"holds={_holds} began={input.HoldBegan} stopped={input.HoldStopped} " +
                      $"pos={_pos} state={resolution.State}");
        }
    }
}
