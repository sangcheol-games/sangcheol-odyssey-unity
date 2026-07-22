using System;
using System.Collections;
using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Game
{
    // ── 흐름 (판정선 1개 = 그룹 1개) ──────────────────────────────────────────
    //
    //  생성/재사용: ChartManager가 Init(시작시각·길이·좌우 X·반환콜백·groupID)으로 구동한다.
    //        startX < endX 이면 LTR로 판단해 캐릭터 방향과 그룹을 세팅한다.
    //        유턴 시에는 풀에 반환하지 않고 방향만 바꿔 다시 Init으로 재사용한다.
    //
    //  이동: 매 프레임 Update() -> UpdatePosition()이 GameManager.GetCurrentTime() 기준 진행도로
    //        startX -> endX 를 보간 이동한다(일시정지 시 시간이 멈춰 자동 정지).
    //        현재 위치(rectTransform)는 HoldStartNote의 홀드바 fill 계산이 실시간으로 읽는다.
    //
    //  소멸: 마디를 지나 화면 밖으로 나가면(CheckOutOfBounds) ReturnToPool() -> onReturn으로 풀에 반환된다.
    // ──────────────────────────────────────────────────────────────────────────
    [RequireComponent(typeof(RectTransform))]
    public class TimelineController : MonoBehaviour
    {
        public RectTransform rectTransform;   // 현재 X 위치. NoteController/HoldStartNote가 판정선 통과 판단에 읽음
        private CanvasGroup canvasGroup;

        [SerializeField]
        private CharacterAnimator _characterAnimator;

        private double startTime;      // 마디 시작 시간 (판정선 출발 시간)
        private double duration;       // 마디 길이 (이동에 걸리는 시간)
        private float startX;         // 출발 X 좌표 (UI 앵커 기준)
        private float endX;           // 도착 X 좌표

        public bool isLTR;            // 왼쪽에서 오른쪽으로 이동하는지 여부

        private Action<TimelineController> onReturn;
        private Func<double> timeProvider;  // 외부 시간 소스 (채보에디터 프리뷰용으로만 사용. 채보에디터도 처음부터 다시 만들 예정이니 없어도 됨)

        private float screenBoundX; // 화면 경계 X 좌표
        private bool isRunning = false;

        void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();

            screenBoundX = Screen.width / 2 + 100f; // TODO: 화면 밖으로 나가는 여유 공간 100px(임시값) -> 정확한 값은 캐릭터 애니메이션 적용 후 수정

        }

        // 판정선을 (재)초기화해 이동을 시작. startX<endX면 LTR로 판단하고 캐릭터 방향/그룹을 세팅한다.
        // ChartManager의 PreloadTimelines(신규 생성)와 StartCurrentBar(유턴 재활용) 양쪽에서 호출된다.
        public void Init(double startTime, double duration, float startX, float endX, Action<TimelineController> returnCallback, int groupID = 0, Func<double> timeProvider = null)
        {
            this.startTime = startTime;
            this.duration = duration;
            this.startX = startX;
            this.endX = endX;
            this.onReturn = returnCallback;
            this.timeProvider = timeProvider;

            isLTR = startX < endX;
            if (_characterAnimator != null)
            {
                _characterAnimator.SetGroupID(groupID);
                _characterAnimator.transform.rotation = isLTR
                    ? Quaternion.Euler(0, 0, 0)
                    : Quaternion.Euler(0, 180, 0);
            }


            rectTransform.anchoredPosition = new Vector2(startX, rectTransform.anchoredPosition.y);

            Activate();
            UpdatePosition();
        }

        void Update()
        {
            if (!isRunning) return;

            UpdatePosition();
        }

        // 현재 게임 시간 기준 진행도(progress)를 계산해 startX→endX로 보간 이동.
        // 시간 소스는 GameManager.GetCurrentTime()(일시정지 시 자동으로 멈춤). timeProvider는 에디터 프리뷰용.
        private void UpdatePosition()
        {
            double currentTime = timeProvider != null
                ? timeProvider()
                : ServiceLocator.Get<IGameManager>().GetCurrentTime();
            double elapsedTime = currentTime - startTime;
            float progress = (float)(elapsedTime / duration);

            // 보간 이동
            float currentX = Mathf.LerpUnclamped(startX, endX, progress);
            rectTransform.anchoredPosition = new Vector2(currentX, rectTransform.anchoredPosition.y);

            CheckOutOfBounds(currentX, progress);
        }

        private void CheckOutOfBounds(float currentX, float progress)
        {
            if (progress > 1.0f && Mathf.Abs(currentX) > screenBoundX)
            {
                ReturnToPool();
            }
        }

        private void Activate()
        {
            SetAlpha(1f);
            isRunning = true;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            ReturnToPool();
        }

        private void ReturnToPool()
        {
            isRunning = false;
            gameObject.SetActive(false);
            onReturn?.Invoke(this);
        }


        private void SetAlpha(float alpha)
        {
            canvasGroup.alpha = alpha;
        }
    }

}
