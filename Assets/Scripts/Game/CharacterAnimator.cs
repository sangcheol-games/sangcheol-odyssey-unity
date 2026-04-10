using System.Collections;
using UnityEngine;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;
using SCOdyssey.App;

namespace SCOdyssey.Game
{
    /// <summary>
    /// 뮤즈대시 스타일 캐릭터 상태 머신 + Root Y 위치 제어 + 애니메이션 핸들러 위임
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _spriteRoot;   // Animator + Sprite 컴포넌트가 있는 자식 오브젝트

        [Header("Y Positions")]
        [SerializeField] private float _topY = 120f;
        [SerializeField] private float _bottomY = 0f;
        [SerializeField] private float _centerY = 60f;

        [Header("Fall Settings")]
        [SerializeField] private float _topFloatDuration = 0.5f;  // topY 유지 시간
        [SerializeField] private float _fallSpeed = 300f;          // bottomY 복귀 속도 (units/sec)

        // 상태
        private CharacterState _currentState = CharacterState.Idle;
        private bool _isTopHolding;
        private bool _isBottomHolding;
        private int _lastHitVariant = -1;

        // Y 이동
        private float _targetY;
        private Coroutine _fallCoroutine;

        // 애니메이션 핸들러
        private ICharacterAnimationHandler _handler;

        // 이벤트 구독용
        private IGameManager _gameManager;

        // ─────────────────────────────────────────────
        // 초기화
        // ─────────────────────────────────────────────

        private void Awake()
        {
            _targetY = _bottomY;
        }

        private void Start()
        {
            // CharacterManager에서 현재 캐릭터 로드
            if (ServiceLocator.TryGet<ICharacterManager>(out var characterManager))
            {
                var skin = characterManager.GetCurrentSkin();
                if (skin != null)
                    LoadCharacter(skin);
            }

            // 게임 이벤트 구독
            if (ServiceLocator.TryGet<IGameManager>(out _gameManager))
            {
                _gameManager.OnNoteJudgedEvent += OnNoteJudgedHandler;
                _gameManager.OnHoldStartEvent  += OnHoldStart;
                _gameManager.OnHoldEndEvent    += OnHoldEnd;
            }
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnNoteJudgedEvent -= OnNoteJudgedHandler;
                _gameManager.OnHoldStartEvent  -= OnHoldStart;
                _gameManager.OnHoldEndEvent    -= OnHoldEnd;
            }
        }

        // ─────────────────────────────────────────────
        // 캐릭터 교체
        // ─────────────────────────────────────────────

        public void LoadCharacter(CharacterSO so)
        {
            _handler = so is SpriteSheetCharacterSO
                ? (ICharacterAnimationHandler)new SpriteSheetAnimationHandler()
                : new SpineAnimationHandler();

            _handler.Initialize(_spriteRoot, so);
            SetState(CharacterState.Idle);
        }


        // ─────────────────────────────────────────────
        // 이벤트 핸들러
        // ─────────────────────────────────────────────

        private void OnNoteJudgedHandler(JudgeType judgeType, NotePosition pos)
        {
            OnNoteHit(pos);
        }

        private void OnHoldStart(NotePosition pos)
        {
            ResetFallTimer();
            if (pos == NotePosition.Top)    _isTopHolding    = true;
            if (pos == NotePosition.Bottom) _isBottomHolding = true;
            UpdateHoldState();
        }

        private void OnHoldEnd(NotePosition pos)
        {
            if (pos == NotePosition.Top)    _isTopHolding    = false;
            if (pos == NotePosition.Bottom) _isBottomHolding = false;
            UpdateHoldState();
        }

        // ─────────────────────────────────────────────
        // 상태 전이 로직
        // ─────────────────────────────────────────────

        private void OnNoteHit(NotePosition pos)
        {
            // 아래 홀드 중 위 히트
            if (pos == NotePosition.Top && _isBottomHolding)
            {
                ResetFallTimer();
                SetState(CharacterState.TopHitWhileBottomHold);
                // bottomY 유지 (_targetY 변경 없음)
                _currentState = CharacterState.TopHitWhileBottomHold;
                return;
            }

            // 위 홀드 중 아래 히트
            if (pos == NotePosition.Bottom && _isTopHolding)
            {
                ResetFallTimer();
                SetState(CharacterState.BottomHitWhileTopHold);
                // topY 유지 (_targetY 변경 없음)
                _currentState = CharacterState.BottomHitWhileTopHold;
                return;
            }

            // 동시 히트 (Middle)
            if (pos == NotePosition.Middle)
            {
                _targetY = _centerY;
                SetState(CharacterState.Middle);
                _currentState = CharacterState.Middle;
                StartFallTimer();  // Middle 애니메이션 후 Fall → bottomY 복귀
                return;
            }

            // Top 히트
            if (pos == NotePosition.Top)
            {
                NotePosition? lane = GetLaneFromState(_currentState);
                if (lane == NotePosition.Top)
                {
                    // 제자리 타격 — Y 변경 없음
                    SetState(PickHitVariant());
                    StartFallTimer();  // 타이머 리셋
                }
                else
                {
                    // 이동 타격 (Fall 포함, 재점프)
                    _targetY = _topY;
                    SetState(CharacterState.Top);
                    StartFallTimer();
                }
                _currentState = CharacterState.Top;
                return;
            }

            // Bottom 히트
            if (pos == NotePosition.Bottom)
            {
                ResetFallTimer();
                NotePosition? lane = GetLaneFromState(_currentState);
                if (lane == NotePosition.Bottom)
                {
                    // 제자리 타격 — Y 변경 없음
                    SetState(PickHitVariant());
                }
                else
                {
                    // 이동 타격 (Fall 포함 → 착지 타격)
                    _targetY = _bottomY;
                    SetState(CharacterState.Bottom);
                }
                _currentState = CharacterState.Bottom;
            }
        }

        private void UpdateHoldState()
        {
            if (_isTopHolding && _isBottomHolding)
            {
                ResetFallTimer();
                _targetY = _centerY;
                SetState(CharacterState.MiddleHold);
                _currentState = CharacterState.MiddleHold;
            }
            else if (_isTopHolding)
            {
                ResetFallTimer();
                _targetY = _topY;
                SetState(CharacterState.TopHold);
                _currentState = CharacterState.TopHold;
            }
            else if (_isBottomHolding)
            {
                ResetFallTimer();
                _targetY = _bottomY;
                SetState(CharacterState.BottomHold);
                _currentState = CharacterState.BottomHold;
            }
            else
            {
                // 홀드 전부 종료 → Fall 하강 후 Idle 복귀
                // (topY/centerY에서 직접 Idle 재생 시 달리기 하강 연출 방지)
                _targetY = _bottomY;
                SetState(CharacterState.Fall);
                _currentState = CharacterState.Fall;
            }
        }

        // ─────────────────────────────────────────────
        // topY 유지 → 자연 낙하
        // ─────────────────────────────────────────────

        private void StartFallTimer()
        {
            if (!gameObject.activeInHierarchy) return;
            ResetFallTimer();
            _fallCoroutine = StartCoroutine(FallAfterDelay());
        }

        private void ResetFallTimer()
        {
            if (_fallCoroutine != null)
            {
                StopCoroutine(_fallCoroutine);
                _fallCoroutine = null;
            }
        }

        private IEnumerator FallAfterDelay()
        {
            yield return new WaitForSeconds(_topFloatDuration);
            _targetY = _bottomY;
            SetState(CharacterState.Fall);
            _currentState = CharacterState.Fall;
            _fallCoroutine = null;
        }

        // ─────────────────────────────────────────────
        // 헬퍼
        // ─────────────────────────────────────────────

        /// <summary>
        /// 현재 상태로부터 레인을 추론. Fall은 어느 레인에도 속하지 않아 null 반환.
        /// </summary>
        private NotePosition? GetLaneFromState(CharacterState s)
        {
            switch (s)
            {
                case CharacterState.Top:
                case CharacterState.TopHold:
                case CharacterState.BottomHitWhileTopHold:
                    return NotePosition.Top;

                case CharacterState.Middle:
                case CharacterState.MiddleHold:
                    return NotePosition.Middle;

                case CharacterState.Idle:
                case CharacterState.Bottom:
                case CharacterState.BottomHold:
                case CharacterState.Hit0:
                case CharacterState.Hit1:
                case CharacterState.Hit2:
                case CharacterState.Hit3:
                case CharacterState.TopHitWhileBottomHold:
                    return NotePosition.Bottom;

                case CharacterState.Fall:
                default:
                    return null;  // 레인 없음 → 항상 이동 타격
            }
        }

        /// <summary>
        /// Hit0~3 중 직전 variant를 제외하고 랜덤 선택
        /// </summary>
        private CharacterState PickHitVariant()
        {
            int pick;
            do
            {
                pick = Random.Range(0, 4);
            } while (pick == _lastHitVariant);

            _lastHitVariant = pick;
            return (CharacterState)(CharacterState.Hit0 + pick);
        }

        private void SetState(CharacterState state)
        {
            _handler?.SetState(state);
        }

        // ─────────────────────────────────────────────
        // Root Y 위치 업데이트
        // ─────────────────────────────────────────────

        private void Update()
        {
            Vector3 pos = transform.localPosition;
            pos.y = Mathf.MoveTowards(pos.y, _targetY, _fallSpeed * Time.deltaTime);
            transform.localPosition = pos;
        }
    }
}
