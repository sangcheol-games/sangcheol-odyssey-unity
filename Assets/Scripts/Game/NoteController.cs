using System;
using SCOdyssey.App;
using SCOdyssey.Config;
using SCOdyssey.Core;
using SCOdyssey.Rhythm;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 흐름 (노트 1개의 생명주기) ────────────────────────────────────────────
    //  노트 오브젝트 1개의 뷰/상태 컨트롤러(타입별 파생: NormalNote/HoldStartNote/HoldEndNote/HoldReleaseNote).
    //  판정은 RhythmSession이 하고, 이 클래스는 표시/상태 전이만 담당한다.
    //
    //  스폰: NoteFieldSpawner.SpawnBar가 풀에서 꺼내 Init(데이터·위치·방향·반환콜백)으로 초기화한다.
    //
    //  상태: SetState(Hidden/Ghost/Active)로 표시를 바꾼다. 고난이도 충돌 시 Hidden으로 숨겼다가,
    //        Update()에서 감시 중인 판정선이 지나가면(CheckGhostState) 스스로 Ghost로 전환한다.
    //        마디가 시작될 때 NoteFieldSpawner가 Active로 올린다.
    //
    //  판정/소멸: 판정되면 NoteFieldSpawner가 OnHit(), 놓치면 OnMiss()를 호출한다.
    //        결국 DeleteNote() -> onReturn 콜백으로 풀에 반환된다(HoldStart는 홀드바도 함께).
    // ──────────────────────────────────────────────────────────────────────────
    public abstract class NoteController : MonoBehaviour
    {
        public NoteData noteData { get; private set; }        // 이 뷰가 보여주는 노트 데이터(id로 판정과 연결)
        protected Action<NoteController> onReturn;            // 풀 반환 콜백(스폰하는 쪽이 Init에서 주입)

        protected Image noteImage;
        protected float holdWidth = 0f;
        protected bool isLTR;
        protected bool isJudged = false;
        protected bool isHoldRemaining = false;  // 판정 후 홀드바가 남아있는 상태
        protected NoteState currentState;
        protected TimelineController trackingTimeline;    // 감시할 타임라인(Hidden→Ghost 전환 판단용)
        protected RectTransform rectTransform;

        protected virtual void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            if (noteImage == null)
                noteImage = GetComponentInChildren<Image>();
        }

        public virtual void Init(NoteData noteData, Vector2 position, bool isLTR, float holdWidth, Action<NoteController> returnCallback)
        {
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

        // 노트 표시 상태 전환(NoteFieldSpawner가 호출). Hidden=투명, Ghost=반투명(설정값), Active=불투명(판정 대상)
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
                    float ghostOpacity = PlayfieldSettingsSO.Shared.ghostAlphaFallback;
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

            // 판정선이 충분히 지나간 뒤에 Ghost로 바꾼다
            float offset = PlayfieldSettingsSO.Shared.hiddenToGhostOffsetPx;

            if (trackingTimeline.isLTR)
            {
                if (timelineX > noteX + offset) isPassed = true;
            }
            else
            {
                if (timelineX < noteX - offset) isPassed = true;
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

        // 노트를 비활성화하고 onReturn 콜백으로 풀에 반환(HoldStart면 홀드바도 함께 회수)
        public void DeleteNote()
        {
            isJudged = true;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }
    }
}
