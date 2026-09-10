using System;
using SCOdyssey.Core;
using SCOdyssey.App;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 흐름 (노트 1개의 생명주기) ────────────────────────────────────────────
    //  노트 오브젝트 1개의 뷰/상태 컨트롤러(타입별 파생: NormalNote/HoldStart/Holding/HoldEnd/HoldReleaseNote).
    //  판정 로직 자체는 ChartManager가 소유하고, 이 클래스는 표시/상태 전이만 담당한다.
    //
    //  스폰: ChartManager.SpawnNextNotes가 풀에서 꺼내 Init(데이터·위치·방향·반환콜백)으로 초기화한다.
    //
    //  상태: SetState(Hidden/Ghost/Active)로 표시를 바꾼다. 고난이도 충돌 시 Hidden으로 숨겼다가,
    //        Update()에서 감시 중인 판정선이 지나가면(CheckGhostState) 스스로 Ghost로 전환한다.
    //        마디가 시작될 때 ChartManager가 Active로 올린다.
    //
    //  판정/소멸: 판정되면 ChartManager가 OnHit(), 놓치면 OnMiss()를 호출한다.
    //        결국 DeleteNote() -> onReturn 콜백으로 풀에 반환된다(HoldStart는 홀드바도 함께).
    // ──────────────────────────────────────────────────────────────────────────
    public abstract class NoteController : MonoBehaviour
    {
        public NoteData noteData { get; private set; }        // ChartManager가 판정에 쓰는 노트 데이터
        protected Action<NoteController> onReturn;            // 풀 반환 콜백(ChartManager가 Init에서 주입)

        protected Image noteImage;
        protected float holdWidth = 0f;
        protected bool isLTR;
        protected bool isJudged = false;
        protected bool isHoldRemaining = false;  // 판정 후 홀드바가 남아있는 상태
        protected NoteState currentState;
        protected TimelineController trackingTimeline;    // 감시할 타임라인(Hidden→Ghost 전환 판단용)
        protected RectTransform rectTransform;

        // 히트 프레임 재생기(NoteImage 자식에 부착). Miss는 아트가 없어 애니메이션 없이 즉시 소멸.
        // 프리팹 하나에 노트 타입 컴포넌트가 전부 붙는 구조(NoteAdapter)라 인스펙터 할당 대신 Awake에서 자동 결선한다.
        [SerializeField] protected NoteHitAnimation hitAnim;

        private bool _isReturned = false;   // DeleteNote 중복 호출 방지(히트 연출 종료 · 홀드바 소모 경로가 겹칠 수 있음)
        private Sprite _idleSprite;         // 풀 재사용 시 복원할 기본 비주얼
        private Color _initialColor;
        private Vector3 _initialScale;

        protected virtual void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null)
                noteImage = GetComponentInChildren<Image>();
            if (hitAnim == null)
                hitAnim = GetComponentInChildren<NoteHitAnimation>(true);

            // Awake는 비활성(enabled=false) 컴포넌트에서도 실행되므로 로그/부수효과 없이 캐시만 한다
            if (noteImage != null)
            {
                _idleSprite = noteImage.sprite;
                _initialColor = noteImage.color;
                _initialScale = noteImage.rectTransform.localScale;
            }
        }

        public virtual void Init(NoteData noteData, Vector2 position, bool isLTR, float holdWidth, Action<NoteController> returnCallback)
        {
            ResetVisualForReuse();

            this.noteData = noteData;
            this.onReturn = returnCallback;
            this.rectTransform.anchoredPosition = position;
            this.isJudged = false;
            this.isHoldRemaining = false;
            this.isLTR = isLTR;
            this.holdWidth = holdWidth;

            trackingTimeline = null;

            SetVisual();
            gameObject.SetActive(true);
        }

        // 풀에서 꺼내 재사용할 때 이전 판정의 잔재(진행 중인 히트 프레임 재생 · 마지막 프레임 스프라이트 등)를 되돌린다.
        private void ResetVisualForReuse()
        {
            _isReturned = false;

            if (hitAnim != null)
                hitAnim.StopAndReset();

            if (noteImage != null)
            {
                noteImage.sprite = _idleSprite;
                noteImage.color = _initialColor;
                noteImage.rectTransform.localScale = _initialScale;
            }
        }

        // 히트 프레임을 재생하고 끝난 뒤 onFinished를 호출한다.
        // 재생기나 프레임이 없으면(아트 미적용 상태) 즉시 onFinished를 불러 판정 흐름이 끊기지 않게 한다.
        protected void PlayHitAnim(Action onFinished)
        {
            if (hitAnim == null || !hitAnim.HasFrames)
            {
                onFinished?.Invoke();
                return;
            }

            hitAnim.Play(onFinished);
        }

        // 노트 표시 상태 전환(ChartManager가 호출). Hidden=투명, Ghost=반투명(설정값), Active=불투명(판정 대상)
        public void SetState(NoteState state)
        {
            currentState = state;
            Color c = noteImage.color;

            switch (state)
            {
                case NoteState.Hidden:
                    c.a = 0f;
                    break;
                case NoteState.Ghost:
                    float ghostOpacity = 0.2f;
                    if (ServiceLocator.TryGet<ISettingsManager>(out var sm))
                        ghostOpacity = sm.Current.noteOpacity;
                    c.a = ghostOpacity;
                    break;
                case NoteState.Active:
                    c.a = 1f;
                    break;
            }
            noteImage.color = c;
            ApplyAlpha(c.a);
        }

        protected virtual void ApplyAlpha(float alpha) { }

        protected abstract void SetVisual();

        // 감시할 판정선 지정. Hidden 노트가 이 판정선이 지나갔는지 스스로 확인하는 데 사용
        public void TrackTimeline(TimelineController timeline)
        {
            trackingTimeline = timeline;
        }

        protected virtual void Update()
        {
            if (isJudged) return;

            // 고난이도 충돌 케이스: Hidden으로 숨겨둔 노트는 감시 중인 판정선이 지나가면 스스로 Ghost로 전환
            if (currentState == NoteState.Hidden && trackingTimeline != null && trackingTimeline.gameObject.activeSelf)
            {
                CheckGhostState();
            }

        }

        // 감시 중인 판정선이 이 노트를 (방향에 맞게) 지나쳤으면 Hidden→Ghost 전환하고 감시 종료
        protected void CheckGhostState()
        {
            float noteX = rectTransform.anchoredPosition.x;
            float timelineX = trackingTimeline.rectTransform.anchoredPosition.x;

            bool isPassed = false;

            const float TIMELINE_OFFSET = 20f; // 판정선이 충분히 지나간 후 ghost로 전환하도록 여유 공간 설정

            if (trackingTimeline.isLTR)
            {
                if (timelineX > noteX + TIMELINE_OFFSET) isPassed = true;
            }
            else
            {
                if (timelineX < noteX - TIMELINE_OFFSET) isPassed = true;
            }

            if (isPassed)
            {
                SetState(NoteState.Ghost); // 판정선이 지나갔으니 고스트로 전환
                trackingTimeline = null; // 더 이상 감시 안 함
            }
        }

        public virtual void OnMiss()
        {
            if (isJudged) return;
            DeleteNote();
        }

        public virtual void OnHit()
        {
            if (isJudged) return;
            DeleteNote();
        }

        // 노트를 비활성화하고 onReturn 콜백으로 ChartManager 풀에 반환(HoldStart면 홀드바도 함께 회수).
        // 여러 경로에서 호출될 수 있어 중복 반환을 막는다(같은 오브젝트가 풀에 두 번 들어가면 노트 1개를 두 곳에서 쓰게 됨).
        public void DeleteNote()
        {
            if (_isReturned) return;
            _isReturned = true;

            isJudged = true;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }
    }
}
