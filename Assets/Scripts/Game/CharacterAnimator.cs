using UnityEngine;
using SCOdyssey.App;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    /// <summary>
    /// 캐릭터 상태 머신. 그룹 단위 입력/판정 이벤트를 받아 Y 위치와 애니메이션을 결정한다.
    /// - 입력(OnLaneInput): Y 이동 + Top/Middle/Bottom 또는 Attack(같은 레인 재입력)
    /// - 판정(OnNoteJudged): Attack 덮어쓰기(Hit0~3/Hit_Kind/Hit_Umm) 또는 전용 크로스 모션
    /// - 홀드(OnHoldStart/End): *Hold 상태 고정
    /// 이동 애니메이션은 히트가 덮어쓰지 않는다.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _spriteRoot;

        [Header("Y Positions")]
        [SerializeField] private float _topY = 120f;
        [SerializeField] private float _bottomY = -120f;
        [SerializeField] private float _centerY = 0f;

        private enum LanePos { Top, Middle, Bottom }

        private int _groupID;
        private LanePos _pos = LanePos.Bottom;
        private CharacterState _currentAnim = CharacterState.Idle;
        private bool _topHold, _bottomHold;
        private int _lastHitVariant = -1;

        // 동일 프레임 내 Top/Bottom 동시 입력을 Middle로 승격하기 위한 버퍼
        private int _lastInputFrame = -1;
        private NotePosition _lastInputPos;

        private ICharacterAnimationHandler _handler;
        private IGameManager _gameManager;

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Start()
        {
            if (ServiceLocator.TryGet<ICharacterManager>(out var characterManager))
            {
                var skin = characterManager.GetCurrentSkin();
                if (skin != null)
                    LoadCharacter(skin);
            }

            if (ServiceLocator.TryGet<IGameManager>(out _gameManager))
            {
                _gameManager.OnLaneInputEvent    += OnLaneInputEvent;
                _gameManager.OnNoteJudgedEvent   += OnNoteJudgedEvent;
                _gameManager.OnHoldStartEvent    += OnHoldStartEvent;
                _gameManager.OnHoldEndEvent      += OnHoldEndEvent;
                _gameManager.OnHoldReleaseEvent  += OnHoldReleaseEvent;
            }
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnLaneInputEvent    -= OnLaneInputEvent;
                _gameManager.OnNoteJudgedEvent   -= OnNoteJudgedEvent;
                _gameManager.OnHoldStartEvent    -= OnHoldStartEvent;
                _gameManager.OnHoldEndEvent      -= OnHoldEndEvent;
                _gameManager.OnHoldReleaseEvent  -= OnHoldReleaseEvent;
            }
        }

        // ─────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────

        public void SetGroupID(int groupID) => _groupID = groupID;

        public void LoadCharacter(CharacterSO so)
        {
            _handler = so is SpriteSheetCharacterSO
                ? (ICharacterAnimationHandler)new SpriteSheetAnimationHandler()
                : new SpineAnimationHandler();

            _handler.Initialize(_spriteRoot, so);
            Play(CharacterState.Idle);
        }

        // ─────────────────────────────────────────────
        // Event routers (groupID 필터)
        // ─────────────────────────────────────────────

        private void OnLaneInputEvent(NotePosition pos, int groupID)
        {
            if (groupID != _groupID) return;

            Debug.Log($"[CA g{_groupID}] OnLaneInput pos={pos} frame={Time.frameCount} lastFrame={_lastInputFrame} lastPos={_lastInputPos} topHold={_topHold} bottomHold={_bottomHold} _pos={_pos} anim={_currentAnim}");

            // 같은 프레임 내 반대 레인 입력 → Middle 승격
            // (ChartManager가 TryJudgeInput에서 동기 발화하므로 두 입력은 같은 frameCount를 공유)
            if (Time.frameCount == _lastInputFrame
                && _lastInputPos != NotePosition.Middle
                && pos != _lastInputPos)
            {
                HandleLaneInput(NotePosition.Middle);
                _lastInputPos = NotePosition.Middle;
                return;
            }

            _lastInputFrame = Time.frameCount;
            _lastInputPos = pos;
            HandleLaneInput(pos);
        }

        private void OnNoteJudgedEvent(JudgeType judge, NotePosition pos, int groupID)
        {
            if (groupID != _groupID) return;
            HandleNoteJudged(judge, pos);
        }

        private void OnHoldStartEvent(NotePosition pos, int groupID)
        {
            if (groupID != _groupID) return;

            // 이미 해당 레인 홀드 상태면 재진입 금지 (애니메이션 재시작 방지)
            // Holding 틱 판정이 연속으로 OnHoldStart를 발화해도 상태/애니메이션 유지
            bool changed = false;
            if (pos == NotePosition.Top    && !_topHold)    { _topHold    = true; changed = true; }
            if (pos == NotePosition.Bottom && !_bottomHold) { _bottomHold = true; changed = true; }

            Debug.Log($"[CA g{_groupID}] HoldStart pos={pos} changed={changed} topHold={_topHold} bottomHold={_bottomHold}");
            if (changed) UpdateHoldState();
        }

        private void OnHoldEndEvent(NotePosition pos, int groupID)
        {
            // 홀드 완주 성공 피드백 전용 (상태 해제는 OnHoldRelease 담당)
            if (groupID != _groupID) return;
            Debug.Log($"[CA g{_groupID}] HoldEnd pos={pos} (feedback only)");
        }

        private void OnHoldReleaseEvent(NotePosition pos, int groupID)
        {
            if (groupID != _groupID) return;

            bool changed = false;
            if (pos == NotePosition.Top    && _topHold)    { _topHold    = false; changed = true; }
            if (pos == NotePosition.Bottom && _bottomHold) { _bottomHold = false; changed = true; }

            Debug.Log($"[CA g{_groupID}] HoldRelease pos={pos} changed={changed} topHold={_topHold} bottomHold={_bottomHold}");
            if (changed) UpdateHoldState();
        }

        // ─────────────────────────────────────────────
        // Handlers
        // ─────────────────────────────────────────────

        private void HandleLaneInput(NotePosition notePos)
        {
            if (_topHold || _bottomHold)
            {
                Debug.Log($"[CA g{_groupID}] HandleLaneInput BLOCKED by hold (topHold={_topHold} bottomHold={_bottomHold})");
                return;
            }

            LanePos target = ToLanePos(notePos);
            if (target == _pos)
            {
                Debug.Log($"[CA g{_groupID}] HandleLaneInput SAME pos={target} → Attack");
                Play(CharacterState.Attack);
                return;
            }

            Debug.Log($"[CA g{_groupID}] HandleLaneInput MOVE {_pos} → {target}, Y={YOf(target)}");
            _pos = target;
            SnapY(YOf(target));
            Play(StateOf(target));
        }

        private void HandleNoteJudged(JudgeType judge, NotePosition notePos)
        {
            // 홀드 중 반대편 히트: 전용 크로스 모션 (Hit 덮어쓰기 없음)
            if (_bottomHold && notePos == NotePosition.Top)
            {
                Play(CharacterState.TopHitWhileBottomHold);
                return;
            }
            if (_topHold && notePos == NotePosition.Bottom)
            {
                Play(CharacterState.BottomHitWhileTopHold);
                return;
            }

            // 이동 애니메이션 보존: 방금 OnLaneInput이 Top/Middle/Bottom을 재생한 경우 유지
            if (IsMovementAnim(_currentAnim)) return;

            // Attack 덮어쓰기: 판정 종류에 따라 히트 애니메이션
            if (_currentAnim == CharacterState.Attack)
            {
                Play(judge switch
                {
                    JudgeType.Kind => CharacterState.Hit_Kind,
                    JudgeType.Umm  => CharacterState.Hit_Umm,
                    _              => PickHitVariant(),
                });
            }
        }

        private void UpdateHoldState()
        {
            if (_topHold && _bottomHold)
            {
                _pos = LanePos.Middle;
                SnapY(_centerY);
                Play(CharacterState.MiddleHold);
            }
            else if (_topHold)
            {
                _pos = LanePos.Top;
                SnapY(_topY);
                Play(CharacterState.TopHold);
            }
            else if (_bottomHold)
            {
                _pos = LanePos.Bottom;
                SnapY(_bottomY);
                Play(CharacterState.BottomHold);
            }
            else
            {
                // 전부 해제 → 현재 위치의 포지션 상태로 복귀 (Fall 없음)
                Play(StateOf(_pos));
            }
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────

        private void Play(CharacterState state)
        {
            _currentAnim = state;
            _handler?.SetState(state);
        }

        private void SnapY(float y)
        {
            if (_spriteRoot == null) return;
            Vector3 p = _spriteRoot.transform.localPosition;
            p.y = y;
            _spriteRoot.transform.localPosition = p;
        }

        private LanePos ToLanePos(NotePosition np) => np switch
        {
            NotePosition.Top    => LanePos.Top,
            NotePosition.Middle => LanePos.Middle,
            _                   => LanePos.Bottom,
        };

        private float YOf(LanePos p) => p switch
        {
            LanePos.Top    => _topY,
            LanePos.Middle => _centerY,
            _              => _bottomY,
        };

        private CharacterState StateOf(LanePos p) => p switch
        {
            LanePos.Top    => CharacterState.Top,
            LanePos.Middle => CharacterState.Middle,
            _              => CharacterState.Bottom,
        };

        private static bool IsMovementAnim(CharacterState s) =>
            s == CharacterState.Top ||
            s == CharacterState.Middle ||
            s == CharacterState.Bottom;

        private CharacterState PickHitVariant()
        {
            int pick;
            do { pick = Random.Range(0, 4); } while (pick == _lastHitVariant);
            _lastHitVariant = pick;
            return (CharacterState)(CharacterState.Hit0 + pick);
        }
    }
}
