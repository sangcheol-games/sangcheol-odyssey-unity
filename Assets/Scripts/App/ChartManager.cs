using System;
using System.Collections.Generic;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 코어 루프 (메서드 호출 순서) ──────────────────────────────────────────
    //
    //  게임 시작 시 Init()을 1회 호출한다.
    //    PrepareNextBar() -> StartCurrentBar() -> StartMusic()
    //
    //  이후 매 프레임 JudgementDriver가 GameManager를 거쳐 SyncTime(songTime, judgeTime)을 호출하고,
    //  SyncTime()은 아래를 순서대로 수행한다.
    //    1) 곡 시각이 마디 종료 시각을 넘었으면  StartCurrentBar() -> CheckGameClear()
    //    2) 레인마다 CheckMissedNotes(), 홀드 중인 레인은 CheckHoldingBody()  (판정 시각 기준)
    //    3) UpdateCountdowns()
    //
    //  키 입력은 같은 프레임에 진행보다 먼저, 입력 시각 순서로 들어온다.
    //    누르면 TryJudgeInput(), 떼면 TryJudgeRelease() 가 호출되고,
    //    판정에 성공하면 ApplyJudgment() 로 마무리한다.
    //
    //  시각: songTime은 곡 시계의 곡 시각(마디 진행·카운트다운), judgeTime은 판정 싱크를 적용한 판정 시각이다
    //        (judgeTime = songTime - 판정 싱크 단계 × 3ms, JudgementDriver가 한 곳에서만 적용한다).
    //        노트 시각과는 judgeTime만 비교한다. [AUDIO-IP:C1]
    //
    //  마디 전환은 StartCurrentBar() 내부에서 일어난다.
    //    ActivateTimelines() -> ActivateGhostNotes() -> currentBarNumber 증가 -> PrepareNextBar()
    //    이때 PrepareNextBar()가 다시 PreloadTimelines() 와 SpawnNextNotes() 를 호출해
    //    "다음 마디"를 미리 준비한다. 즉 매 마디 이 사이클이 반복되며, 현재 마디를 스크롤하는
    //    동안 다음 마디는 항상 한 발 앞서 준비돼 있다.
    // ──────────────────────────────────────────────────────────────────────────
    public class ChartManager : MonoBehaviour
    {
        private IGameManager gameManager;

        public RectTransform noteParent;
        public RectTransform timelineParent;
        public Transform objectPoolParent;

        private double currentTime;


        [Header("노트 풀링")]
        public GameObject notePrefab;
        private Queue<GameObject> notePool = new Queue<GameObject>();

        [Header("레이어 분리")]
        public RectTransform holdLayer;     // HoldBar용 Canvas (Inspector 할당)
        public RectTransform headLayer;     // NoteHead용 Canvas (Inspector 할당)
        public GameObject holdBarPrefab;    // 루트(RectMask2D 뷰포트) > Fill(아트) 2단 구조 프리팹 (Inspector 할당)
        private Queue<GameObject> holdBarPool = new Queue<GameObject>();

        [Header("이펙트 풀링")]
        public GameObject effectPrefab;
        private Queue<GameObject> effectPool = new Queue<GameObject>();


        [Header("판정선")]
        public GameObject timelinePrefab; // 판정선 프리팹
        private Queue<GameObject> timelinePool = new Queue<GameObject>();
        public RectTransform[] timelineTransforms = new RectTransform[2];   // 판정선의 상하 위치 좌표
        // 판정선 상태 2단계: preloaded(다음 마디용으로 생성만 됨) → active(현재 마디에서 실제 이동 중). key = 그룹ID(0/1)
        private Dictionary<int, TimelineController> activeTimelines = new Dictionary<int, TimelineController>();
        private Dictionary<int, TimelineController> preloadedTimelines = new Dictionary<int, TimelineController>();



        [Header("레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)



        [Header("마디 진행 관리")]
        private Queue<LaneData> remainingChart; // chartData 내의 모든 LaneData를 복사하여 사용 (아직 등장 안 한 전체 마디)
        private List<LaneData> currentBarLanes = new List<LaneData>();  // 현재 스크롤 중인 마디의 LaneData 리스트
        private Queue<LaneData> nextBarLanes = new Queue<LaneData>();   // 다음 스크롤을 준비 중인 마디의 LaneData 리스트

        private int currentBarNumber = 0;      // 현재 마디 인덱스(0부터). StartCurrentBar 승격 시 ++
        private double currentBarEndTime = 0f; // 현재 마디의 종료 시간. SyncTime에서 currentTime이 이 값을 넘으면 다음 마디로 전환
        private double barDuration = 0f; // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM
        private double nextBarRevealTime = double.MaxValue; // 다음 마디 노트를 Active 색으로 표시할 곡 시각(= 판정선이 화면 가장자리에 들어오는 시각)

        public Image[] countdownImages = new Image[LANE_COUNT];

        // GameUI_Countdown_1~3. 인덱스 = 표시 숫자 - 1
        [SerializeField] private Sprite[] countdownSprites = new Sprite[3];

        private LaneState[] _lanes;   // LANE_COUNT(=4) 크기. 레인별 런타임 상태

        // 레인 1개의 런타임 상태 묶음
        private class LaneState
        {
            public readonly Queue<NoteController> activeNotes = new Queue<NoteController>();  // 판정 대기 노트 FIFO(가장 앞 = 다음 판정 대상)
            public readonly Queue<NoteController> ghostNotes = new Queue<NoteController>();   // 스폰됐지만 아직 활성화 전인 노트 버퍼(Waiting/Ghost)
            public bool isHolding;             // 현재 이 레인 키를 누르고 있는지
            public double? bufferedInput;      // 마디 전환 직전 선입력 시각(노트 활성화 후 FlushBufferedInput에서 재판정)
            public double countdownTargetTime; // 카운트다운이 0이 되는 목표 시각(다음 마디 시작 시각)
            public bool isCountdownActive;     // 카운트다운 UI 표시 중 여부
            public int countdownDisplayNum;    // 현재 표시 중인 카운트다운 숫자(0 = 표시 없음). 중복 갱신 방지용
        }


        private Action<JudgeType, NoteController> judgeEffectAction;

        // 판정 등급별 타격음 파일명. 인덱스 = (int)JudgeType (Perfect/Master/Ideal/Kind/Umm 순) — 순서를 바꾸면 안 된다.
        // StreamingAssets/HitSound/ 기준. 소리를 바꾸려면 같은 이름으로 WAV를 덮어쓰면 코드 수정 없이 교체된다.
        // TODO: 노트 스킨별 타격음을 지원하게 되면 이 테이블을 NoteSkinSO로 올리고
        //       Init()에서 GetCurrentSkin()으로 파일명을 받아오도록 바꾼다(오디오 API는 그대로).
        private static readonly string[] HitSoundFiles =
        {
            "hit_perfect.wav",
            "hit_master.wav",
            "hit_ideal.wav",
            "hit_kind.wav",
            "hit_umm.wav",
        };

        // [AUDIO-IP:C2] 타격음(원샷)과 판정 오차 기록
        private IOneShotPlayer _oneShots;
        private IJudgementTimingLog _timingLog;
        private readonly OneShotId[] _hitSounds = new OneShotId[HitSoundFiles.Length];

        // 다음 마디 준비 시 재사용하는 임시 버퍼(판정선 생성/재활용/제거 판단용 스크래치)
        private readonly HashSet<int> _nextGroupsBuffer = new HashSet<int>();        // 다음 마디에 등장할 그룹 ID 집합
        private readonly Dictionary<int, bool> _nextGroupDirBuffer = new Dictionary<int, bool>(); // 그룹별 진행 방향(isLTR)
        private readonly List<int> _groupsToRemoveBuffer = new List<int>();          // 이번 전환에서 비활성화할 그룹 목록


        void Awake()
        {
            _lanes = new LaneState[LANE_COUNT];
            for (int i = 0; i < LANE_COUNT; i++)
                _lanes[i] = new LaneState();
        }

        /// <summary>
        /// 게임 시작 시 GameManager가 1회 호출. 채보를 큐에 적재하고 설정을 로드한 뒤
        /// 첫 마디를 준비/시작하고 음원 재생을 예약한다.
        /// 흐름: 설정 로드 → barDuration 계산 → PrepareNextBar → StartCurrentBar → StartMusic.
        /// </summary>
        public void Init(ChartData chartData, IGameManager gameManager)
        {
            this.gameManager = gameManager;
            remainingChart = new Queue<LaneData>(chartData.GetFullChartList());
            currentBarNumber = 0;

            // 판정 싱크는 JudgementDriver가 판정 시각(judgeTime)에 적용해 넘겨준다. 여기서는 읽지 않는다. [AUDIO-IP:C1]

            // [AUDIO-IP:C2] 타격음 id 확보. Register는 파일명 기준 멱등이라 리트라이로 다시 불려도 재로드가 없다.
            // 곡 시계는 Init() 안의 StartMusic → 세션 Start 뒤 커밋될 때 흐르기 시작하므로 이 로드 비용은 곡 시각에 영향이 없다.
            if (ServiceLocator.TryGet<IOneShotPlayer>(out _oneShots))
            {
                for (int i = 0; i < HitSoundFiles.Length; i++)
                    _hitSounds[i] = _oneShots.Register(HitSoundFiles[i]);
            }
            ServiceLocator.TryGet<IJudgementTimingLog>(out _timingLog);

            // TODO: 4/4박자가 아닐경우의 barDuration 계산 (BPM 기반)
            barDuration = 60f / chartData.bpm * 4f; // 4/4박자 기준
            currentBarEndTime = 0f + barDuration;

            for (int i = 0; i < LANE_COUNT; i++)
            {
                _lanes[i].activeNotes.Clear();
                _lanes[i].ghostNotes.Clear();
                _lanes[i].isHolding = false;
                _lanes[i].bufferedInput = null;
                _lanes[i].isCountdownActive = false;

                _lanes[i].countdownDisplayNum = 0;

                countdownImages[i].enabled = false;
                countdownImages[i].gameObject.SetActive(false);
            }

            PrepareNextBar();   // 0번(빈) 마디 이후 첫 마디를 미리 준비
            StartCurrentBar();  // 준비된 마디를 현재 마디로 승격 + 다음 마디 선행 준비

            // barDuration만큼 음원 재생을 지연 → 0번 빈 마디가 흐르는 동안 1번 마디를 준비할 시간을 확보
            gameManager.StartMusic(barDuration);
        }

        /// <summary>
        /// 매 프레임 GameManager가 곡 시각과 판정 시각을 주입하는 진입점. [AUDIO-IP:C4]
        /// 마디 종료 시각을 넘으면 다음 마디로 전환하고(songTime), 레인별 miss/holding 판정(judgeTime)과 카운트다운(songTime)을 갱신한다.
        /// </summary>
        public void SyncTime(double songTime, double judgeTime)
        {
            this.currentTime = songTime;

            // 현재 마디 종료 시각을 넘어서면 다음 마디로 전환하고 종료 조건도 확인
            if (remainingChart.Count >= 0 && currentTime >= currentBarEndTime)
            {
                StartCurrentBar();
                CheckGameClear();
            }

            // 다음 마디 판정선이 화면에 들어왔으면 다음 마디 노트를 미리 Active 색으로 표시(판정 큐는 그대로)
            if (currentTime >= nextBarRevealTime) RevealNextBarNotes();

            for (int i = 0; i < LANE_COUNT; i++)
            {
                CheckMissedNotes(i, judgeTime);                          // 판정 윈도우를 지나친 노트 miss 처리
                if (_lanes[i].isHolding) CheckHoldingBody(i, judgeTime); // 홀드 중이면 Holding/HoldEnd 본체 판정
            }

            UpdateCountdowns();

        }
        
        /// <summary>
        /// 게임 종료 조건을 모두 만족하는지 검사. 만족 시 GameManager.OnGameFinished 호출.
        /// 조건: 남은 마디 0 + 준비 중 마디 0 + 모든 레인의 active/ghost 큐 비움 + 음원 종료.
        /// </summary>
        private void CheckGameClear()
        {
            // 게임이 이미 종료되었으면 중복 호출 방지
            if (!gameManager.IsGameRunning) return;

            if (remainingChart.Count > 0) return;
            if (nextBarLanes.Count > 0) return;

            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (_lanes[i].activeNotes.Count > 0) return;
                if (_lanes[i].ghostNotes.Count > 0) return;
            }

            // 음악이 아직 재생 중이면 대기
            if (gameManager.IsAudioPlaying) return;

            Debug.Log("Game Cleared.");
            gameManager.OnGameFinished();
        }


        #region Bar
        /// <summary>
        /// 다음 마디를 "준비만" 한다(아직 현재 마디로 만들지 않음).
        /// remainingChart에서 다음 마디에 해당하는 LaneData를 모두 nextBarLanes로 옮긴 뒤
        /// 판정선을 프리로드하고 노트를 Waiting/Ghost 상태로 미리 스폰한다.
        /// </summary>
        private void PrepareNextBar()
        {
            nextBarLanes.Clear();

            int nextBar = currentBarNumber;

            while (remainingChart.Count > 0)    // 다음 마디에 해당하는 모든 LaneData를 remaingChart => nextBarLanes로 이동
            {
                LaneData nextLane = remainingChart.Peek();
                if (nextLane.bar > nextBar) break;  // 다음 마디 번호를 넘어서면 중단(정렬돼 있으므로 앞부분만 소비)

                nextBarLanes.Enqueue(nextLane);
                remainingChart.Dequeue();
            }

            nextBarRevealTime = double.MaxValue;

            if (nextBarLanes.Count > 0)
            {
                PreloadTimelines();  // 이 마디에 필요한 판정선 생성/재활용 준비
                SpawnNextNotes();    // 노트 오브젝트를 Waiting/Ghost로 미리 스폰
                nextBarRevealTime = CalcNextBarRevealTime();
            }

        }

        /// <summary>
        /// 다음 마디 판정선이 화면 가장자리를 넘어 들어오는 곡 시각.
        /// 판정선은 endpoint 바깥(진행도 &lt; 0)부터 시간에 선형으로 이동하므로 위치 대신 시간으로 계산한다.
        /// 마디 첫 비트 노트(endpoint)가 판정 순간에야 Active로 바뀌는 문제를 피하려고, 이 시각에 미리 Active 색으로 표시한다.
        /// 방향이 섞이면 늦게 들어오는 쪽(바깥 여백이 작은 쪽)에 맞춘다. 화면 끝이 endpoint 안쪽이면 리드 0(마디 시작과 동일).
        /// </summary>
        private double CalcNextBarRevealTime()
        {
            float half = ((RectTransform)leftEndpoint.parent).rect.width * 0.5f;   // 캔버스 단위 화면 가장자리
            float left = leftEndpoint.anchoredPosition.x;
            float right = rightEndpoint.anchoredPosition.x;
            float laneWidth = right - left;

            float margin = float.MaxValue;
            foreach (var lane in nextBarLanes)
            {
                float m = lane.isLTR ? left + half : half - right;
                if (m < margin) margin = m;
            }

            double lead = laneWidth > 0f ? barDuration * Mathf.Max(0f, margin) / laneWidth : 0d;
            return currentBarNumber * barDuration - lead;   // PreloadTimelines의 nextStartTime 기준
        }

        /// <summary>
        /// 준비돼 있던 다음 마디를 "현재 마디"로 승격시키는 파이프라인의 핵심.
        /// 판정선 재활용(유턴) 판정 → 프리로드 판정선/고스트 노트 활성화 → 다음 마디 선행 준비까지 수행한다.
        /// </summary>
        private void StartCurrentBar()
        {
            // 1) 이번 마디의 시작/종료 시각 계산
            double startTime = currentBarNumber * barDuration;
            currentBarEndTime = startTime + barDuration;

            if (nextBarLanes.Count == 0)
            {
                Debug.Log("End of Chart Reached.");
                return;
            }

            // 2) 이번 마디에 등장할 그룹과 방향 수집
            _nextGroupsBuffer.Clear();
            _nextGroupDirBuffer.Clear();

            foreach (var lane in nextBarLanes)
            {
                int groupID = GetTrackGroupID(lane.line - 1);
                _nextGroupsBuffer.Add(groupID);
                _nextGroupDirBuffer[groupID] = lane.isLTR;
            }

            // 3) 이미 이동 중인 판정선 처리: 같은 그룹인데 방향이 반대면 재활용(유턴), 그 외는 제거 목록에
            _groupsToRemoveBuffer.Clear();

            foreach (var kvp in activeTimelines)
            {
                int groupID = kvp.Key;
                TimelineController timeline = kvp.Value;

                if (_nextGroupsBuffer.Contains(groupID) && timeline.isLTR != _nextGroupDirBuffer[groupID])
                {
                    // 재활용: 풀에 반환하지 않고 방향을 뒤집어 새 마디로 다시 Init (캐릭터 중복 교차 방지)
                    bool isLTR = _nextGroupDirBuffer[groupID];
                    float startX, endX;
                    GetTimelinePositions(isLTR, out startX, out endX);

                    timeline.Init(
                        startTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); },
                        groupID: groupID
                    );
                }
                else
                {
                    _groupsToRemoveBuffer.Add(groupID);   // 이번 마디에 안 쓰거나 방향 동일 → activeTimelines에서 뺌
                }
            }

            foreach (int id in _groupsToRemoveBuffer) activeTimelines.Remove(id);

            // 4) 준비된 판정선/노트를 실제 활성 상태로 승격
            ActivateTimelines();   // preloadedTimelines → activeTimelines
            ActivateGhostNotes();  // ghostNotes → activeNotes (Active 상태로)


            currentBarLanes = new List<LaneData>(nextBarLanes);

            // 5) 마디 번호 증가 후, 그 다음 마디를 다시 선행 준비 (항상 한 마디 앞서 준비 유지)
            currentBarNumber++;
            PrepareNextBar();

        }
        // 레인 인덱스(0~3) → 그룹 ID. 0~1 = 그룹0(상단), 2~3 = 그룹1(하단)
        private int GetTrackGroupID(int laneIndex)
        {
            return laneIndex <= 1 ? 0 : 1;
        }

        #endregion


        /// <summary>
        /// 매 프레임 호출. 활성 카운트다운 레인에 대해 다음 마디 시작까지 남은 ¼마디 비트 수를 3/2/1 스프라이트로 표시.
        /// 목표 시각에 도달하면(남은 시간 &lt;= 0) 이미지를 끄고 비활성화한다.
        /// </summary>
        private void UpdateCountdowns()
        {
            double beatDuration = barDuration / 4.0d;   // ¼마디 = 카운트다운 1칸

            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (!_lanes[i].isCountdownActive) continue;

                double timeDiff = _lanes[i].countdownTargetTime - currentTime;

                if (timeDiff <= 0)
                {
                    _lanes[i].countdownDisplayNum = 0;
                    countdownImages[i].gameObject.SetActive(false);
                    _lanes[i].isCountdownActive = false;
                    continue;
                }

                double remainingBeats = timeDiff / beatDuration;

                if (remainingBeats <= 3.01d)
                {
                    int displayNum = (int)Math.Ceiling(remainingBeats);     // 올림 처리

                    if (displayNum > 0 && displayNum <= 3)
                    {
                        SetCountdownNumber(i, displayNum);
                    }
                }
                else
                {
                    SetCountdownNumber(i, 0);
                }

            }
        }

        // 레인 카운트다운 표시 숫자를 갱신한다. num = 0 이면 이미지를 숨긴다.
        // 값이 바뀔 때만 sprite를 대입해 ContentSizeFitter의 불필요한 레이아웃 리빌드를 막는다.
        private void SetCountdownNumber(int laneIndex, int num)
        {
            if (_lanes[laneIndex].countdownDisplayNum == num) return;
            _lanes[laneIndex].countdownDisplayNum = num;

            Image image = countdownImages[laneIndex];

            if (num <= 0 || num > countdownSprites.Length)
            {
                image.enabled = false;
                return;
            }

            image.sprite = countdownSprites[num - 1];
            image.enabled = true;
        }

        // 특정 카운트다운 슬롯을 켠다. 이미 같은 목표 시각으로 켜져 있으면 중복 설정 방지
        private void ActivateCountdown(int index, double targetTime)
        {
            if (_lanes[index].isCountdownActive && Math.Abs(_lanes[index].countdownTargetTime - targetTime) < 0.01d) return;

            _lanes[index].countdownDisplayNum = 0;

            countdownImages[index].enabled = false;   // 3칸 이내로 들어올 때까지는 빈 상태
            countdownImages[index].gameObject.SetActive(true);

            _lanes[index].countdownTargetTime = targetTime;
            _lanes[index].isCountdownActive = true;
        }





        #region Timeline

        /// <summary>
        /// 다음 마디에 필요한 판정선을 준비한다.
        /// 방향이 뒤집힌 기존 판정선이 있으면 재활용 대상으로 두어 새로 만들지 않고(재활용은 StartCurrentBar에서 수행),
        /// 없으면 풀에서 새 판정선을 화면 밖에 생성해 preloadedTimelines에 넣는다. 카운트다운도 함께 켠다.
        /// </summary>
        private void PreloadTimelines()
        {
            _nextGroupsBuffer.Clear();
            _nextGroupDirBuffer.Clear();

            foreach (var laneData in nextBarLanes)
            {
                int groupID = GetTrackGroupID(laneData.line - 1);
                if (!_nextGroupsBuffer.Contains(groupID))
                {
                    _nextGroupsBuffer.Add(groupID);
                    _nextGroupDirBuffer[groupID] = laneData.isLTR;
                }
            }

            double nextStartTime = currentBarNumber * barDuration;

            foreach (int groupID in _nextGroupsBuffer)
            {
                // 이미 이동 중인 판정선이 방향만 반대면 재활용 대상 → 여기서는 새로 만들지 않음
                bool isReused = false;
                if (activeTimelines.TryGetValue(groupID, out var existing) &&
                    existing.isLTR != _nextGroupDirBuffer[groupID])
                {
                    isReused = true;
                }

                if (!isReused)  // 새 판정선 생성
                {
                    TimelineController timeline = GetTimelineFromPool();
                    timeline.transform.SetParent(timelineParent, false);
                    timeline.transform.position = timelineTransforms[groupID].position;

                    bool isLTR = _nextGroupDirBuffer[groupID];
                    float startX, endX;
                    GetTimelinePositions(isLTR, out startX, out endX);

                    timeline.Init(
                        nextStartTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); },
                        groupID: groupID
                    );

                    preloadedTimelines.Add(groupID, timeline);
                }

                // 카운트다운은 방향에 따라 좌/우 슬롯이 달라짐: 그룹당 2슬롯 중 LTR=0, RTL=1
                int uiIndex = (groupID * 2) + (_nextGroupDirBuffer[groupID] ? 0 : 1);
                ActivateCountdown(uiIndex, nextStartTime);
            }

        }

        // 프리로드된 판정선을 activeTimelines로 승격(실제 이동 시작). StartCurrentBar에서 호출
        private void ActivateTimelines()
        {
            foreach (var kvp in preloadedTimelines)
            {
                int groupID = kvp.Key;
                TimelineController timeline = kvp.Value;
                
                if (!activeTimelines.TryAdd(groupID, timeline))
                {
                    Debug.LogWarning("Timeline 승격 중복 발생: 그룹 " + groupID);
                    ReturnTimelineToPool(timeline.gameObject);
                }
            }
            preloadedTimelines.Clear();
        }

        // 진행 방향에 따른 시작/끝 X 좌표. LTR이면 좌→우, RTL이면 우→좌 (엔드포인트 기준)
        private void GetTimelinePositions(bool isLTR, out float startX, out float endX)
        {
            if (isLTR)
            {
                startX = leftEndpoint.anchoredPosition.x;
                endX = rightEndpoint.anchoredPosition.x;
            }
            else
            {
                startX = rightEndpoint.anchoredPosition.x;
                endX = leftEndpoint.anchoredPosition.x;
            }
        }

        #endregion



        #region Note
        /// <summary>
        /// nextBarLanes의 각 노트를 풀에서 꺼내 위치를 계산해 배치하고, 초기 상태(Waiting/Ghost)를 결정한 뒤
        /// 레인별 ghostNotes 큐에 적재한다. (아직 판정 대상 아님 → StartCurrentBar에서 Active 승격)
        /// </summary>
        private void SpawnNextNotes()
        {
            foreach (var lane in nextBarLanes)
            {
                // 노트 간격 = 레인 전체 폭 / 비트 수 (마디를 비트 수만큼 균등 분할)
                float laneWidth = rightEndpoint.anchoredPosition.x - leftEndpoint.anchoredPosition.x;
                float noteInterval = laneWidth / lane.beat;

                // 레인 y좌표 기준점 획득
                RectTransform laneRT = laneTransforms[lane.line - 1];
                // 노트 배치 시작점 x좌표 위치
                float laneStartX = lane.isLTR ? leftEndpoint.anchoredPosition.x : rightEndpoint.anchoredPosition.x;

                int groupID = GetTrackGroupID(lane.line - 1);

                // 충돌 = 현재 이동 중인 판정선과 같은 그룹을 다음 마디에서도 사용하는 경우(고난이도).
                // 다음 마디 노트가 현재 판정선 앞쪽에 함께 보이므로, 어둡고 작게(Waiting) 미리 보여주되 판정선이 지나간 뒤에만 Active로 올린다.
                // (판정선 앞의 밝은 노트 = 이번에 칠 노트 규칙 유지)
                TimelineController currentTimeline = null;
                bool isConflict = false;    // 현재 마디와 다음마디가 동일 그룹으 사용할 경우
                bool currentIsLTR = true;

                if (activeTimelines != null && activeTimelines.TryGetValue(groupID, out TimelineController timeline))
                {
                    isConflict = true;
                    currentTimeline = timeline;
                    currentIsLTR = currentTimeline.isLTR;
                }

                foreach (var noteData in lane.Notes)
                {
                    GameObject note = GetNoteFromPool();
                    note.transform.SetParent(headLayer, false);   // 노트 헤드는 headLayer(홀드바보다 위 레이어)에 배치

                    NoteAdapter noteAdapter = note.GetComponent<NoteAdapter>();

                    // 어댑터가 노트 타입에 맞는 컨트롤러 컴포넌트를 활성화해 반환(하나의 프리팹이 모든 타입 보유)
                    NoteController noteController = noteAdapter.ActivateAndGet(noteData.noteType);

                    // 배치 위치: 시작점 + 간격 × 노트 인덱스 × 방향부호. y는 레인 기준점
                    Vector2 spawnPos = new Vector2(
                        laneStartX + noteInterval * noteData.index * (lane.isLTR ? 1 : -1),
                        laneRT.anchoredPosition.y
                    );

                    // HoldStart는 holdBarBeats * noteInterval로 실제 홀드바 길이 계산
                    float holdWidth = noteData.noteType == NoteType.HoldStart
                        ? noteInterval * (noteData.holdBarBeats ?? 1)
                        : noteInterval;

                    // HoldStart는 홀드바를 별도 풀에서 꺼내 부착하고, 반환 콜백에서 노트와 홀드바를 함께 회수
                    if (noteData.noteType == NoteType.HoldStart)
                    {
                        GameObject holdBar = GetFromPool(holdBarPool, holdBarPrefab);
                        holdBar.transform.SetParent(holdLayer, false);
                        holdBar.transform.SetAsFirstSibling();  // 헤드처럼 나중 마디 홀드바가 뒤에 그려지도록(현재 마디 홀드바를 덮지 않게)
                        ((HoldStartNote)noteController).SetHoldBar(holdBar);
                        noteController.Init(
                            noteData,
                            spawnPos,
                            lane.isLTR,
                            holdWidth,
                            (returnedNote) =>
                            {
                                ReturnToPool(holdBarPool, holdBar);
                                ReturnNoteToPool(returnedNote.gameObject);
                            }
                        );
                    }
                    else
                    {
                        noteController.Init(
                            noteData,
                            spawnPos,
                            lane.isLTR,
                            holdWidth,
                            (returnedNote) => { ReturnNoteToPool(returnedNote.gameObject); }
                        );
                    }

                    if (isConflict && currentTimeline != null)
                    {
                        // 같은 그룹 충돌: 노트가 현재 타임라인의 endpoint에 위치하는지 확인
                        // endpoint 노트 = 유턴 패턴에서 다음 마디 첫 비트(같은 방향 연속이면 endpoint에 놓이지 않음).
                        // 판정선이 절대 지나칠 수 없어 Waiting으로 두면 마디 시작까지 풀리지 않으므로 Ghost로 둔다(다른 노트처럼 RevealNextBarNotes에서 Active)
                        float noteX = spawnPos.x;
                        bool atEndpoint = currentIsLTR
                            ? Mathf.Approximately(noteX, rightEndpoint.anchoredPosition.x)  // LTR: rightEndpoint
                            : Mathf.Approximately(noteX, leftEndpoint.anchoredPosition.x);  // RTL: leftEndpoint

                        if (!atEndpoint)
                        {
                            // endpoint가 아님: 어둡고 작게 보이다가, 판정선이 지나가면 Ghost로 전환(이후 RevealNextBarNotes가 Active로 올리며 원래 크기로)
                            noteController.WaitForTimelinePass(currentTimeline, holdLayer);
                        }
                        else
                        {
                            // endpoint에 위치(유턴 첫 비트): 다른 노트처럼 Ghost로 노출
                            noteController.SetState(NoteState.Ghost);

                            // 홀드바는 endpoint에서 판정선 쪽으로 뻗어 아직 판정 전인 현재 마디 노트와 겹치므로 마디 시작까지 Ghost 유지
                            if (noteData.noteType == NoteType.HoldStart)
                                ((HoldStartNote)noteController).KeepHoldBarGhost = true;
                        }
                    }
                    else
                    {
                        // 충돌 없음: 바로 Ghost(어둡고 작게)로 노출
                        noteController.SetState(NoteState.Ghost);
                    }

                    _lanes[lane.line - 1].ghostNotes.Enqueue(noteController);   // 판정 대상 아님. StartCurrentBar에서 Active로 승격
                }
            }
        }

        /// <summary>
        /// 다음 마디 판정선이 화면에 들어온 뒤 매 프레임 호출. ghostNotes 중 Ghost 상태인 노트만 Active 색으로 바꾼다(표시만).
        /// 판정 큐 승격은 여전히 StartCurrentBar → ActivateGhostNotes에서 한다.
        /// Waiting은 건드리지 않는다: 현재 판정선이 아직 지나가지 않은 노트라 밝히면 현재 마디 노트와 구분되지 않고,
        /// Waiting HoldStart는 이전 마디 판정선을 추적 중이라 Active가 되면 홀드바가 잘못 깎인다.
        /// 판정선이 지나가 Ghost로 바뀌면 다음 프레임에 이 루프가 바로 Active로 올린다.
        /// </summary>
        private void RevealNextBarNotes()
        {
            for (int i = 0; i < LANE_COUNT; i++)
            {
                foreach (NoteController note in _lanes[i].ghostNotes)
                {
                    if (note.State != NoteState.Ghost) continue;

                    note.SetState(NoteState.Active);

                    // 유턴 첫 비트 HoldStart: 헤드만 Active, 홀드바는 마디 시작(ActivateGhostNotes)까지 Ghost
                    if (note is HoldStartNote holdStart && holdStart.KeepHoldBarGhost)
                        holdStart.SetHoldBarState(NoteState.Ghost);
                }
            }
        }

        /// <summary>
        /// 마디 시작 시, 모든 레인의 ghostNotes를 Active로 올려 activeNotes(판정 대상)로 이동시킨다.
        /// HoldStart는 홀드바 fill 애니메이션을 위해 판정선 추적을 연결하고, 선입력 버퍼가 있으면 flush한다.
        /// </summary>
        private void ActivateGhostNotes()
        {
            for (int i = 0; i < LANE_COUNT; i++)
            {
                while (_lanes[i].ghostNotes.Count > 0)
                {
                    NoteController note = _lanes[i].ghostNotes.Dequeue();
                    note.SetState(NoteState.Active);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    // Holding/HoldEnd는 비주얼 없으므로 추적 불필요
                    if (note.noteData.noteType == NoteType.HoldStart)
                    {
                        int groupID = GetTrackGroupID(i);
                        if (activeTimelines.TryGetValue(groupID, out var timeline))
                        {
                            note.TrackTimeline(timeline);
                        }
                    }

                    _lanes[i].activeNotes.Enqueue(note);
                    FlushBufferedInput(i);
                }
            }
        }

        #endregion


        #region Judgement
        /// <summary>
        /// 키를 눌렀을 때 호출(GameManager가 라우팅). 해당 레인 activeNotes의 맨 앞 노트를 판정 윈도우로 판정한다.
        /// 노트가 아직 없으면 선입력으로 버퍼링. Normal/HoldStart만 눌러서 판정(홀드 본체/릴리즈는 별도 경로).
        /// </summary>
        public void TryJudgeInput(int laneIndex, double judgeTime)
            => TryJudgeInput(laneIndex, judgeTime, isFreshPress: true);

        /// <summary>
        /// isFreshPress: 플레이어가 실제로 키를 누른 순간인지 여부. FlushBufferedInput에서 재진입할 때는 false.
        /// 선입력이 버퍼링되는 순간 PredictInput이 ghostNotes로 예측 판정을 끝내고 타격음과 입력 이벤트를
        /// 이미 내보냈으므로, flush에서 또 내면 한 번의 입력에 소리와 캐릭터 연출이 두 번 나간다.
        ///
        /// ★ 이 플래그가 "OnLaneInput은 물리적 키 입력당 정확히 1회"라는 불변식을 지킨다.
        ///   CharacterAnimator의 입력 버퍼 해석이 이 불변식 위에 서 있으므로 깨뜨리면 안 된다.
        /// </summary>
        private void TryJudgeInput(int laneIndex, double judgeTime, bool isFreshPress)
        {
            int listIndex = laneIndex - 1;  // 인덱스 보정

            if (isFreshPress)
            {
                // 판정 예측을 한 번만 하고 타격음과 캐릭터 연출이 같은 값을 쓰게 한다.
                // 타격음을 가장 먼저 재생한다. 판정·이펙트보다 앞에 둬서 지연을 최소화
                // (ApplyJudgment는 이펙트 풀이 비면 Instantiate까지 하므로 그 뒤에 내면 스파이크만큼 밀린다)
                (JudgeType sound, PressResult result) prediction = PredictInput(listIndex, judgeTime);
                // 노트를 실제로 칠 때만 타격음. 헛침(NoTarget)·홀드 재그립(HoldBody)은 소리 없음
                if (prediction.result == PressResult.HitTarget)
                    PlayHitSound(prediction.sound);

                LaneInputResult inputResult = new LaneInputResult(
                    GetNotePosition(listIndex),
                    GetTrackGroupID(listIndex),
                    prediction.sound,
                    prediction.result,
                    judgeTime);
                gameManager.OnLaneInput(inputResult);
            }

            // flush 경로에서도 실행되어야 한다. 홀드 본체 판정(CheckHoldingBody)이 이 플래그를 본다.
            _lanes[listIndex].isHolding = true;

            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0)
            {
                // 마디 전환 직전 선입력: 노트가 활성화되면 FlushBufferedInput에서 재판정
                _lanes[listIndex].bufferedInput = judgeTime;
                return;
            }

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.Normal && targetNote.noteData.noteType != NoteType.HoldStart) return;

            // 판정 싱크는 judgeTime에 이미 들어 있다(윈도우 중심 = noteTime + 판정 싱크) [AUDIO-IP:C1]
            double timeDiff = Math.Abs(judgeTime - targetNote.noteData.time);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                //Debug.Log("판정 범위 밖 입력");
                return;
            }

            JudgeType judge = GetJudgeType(timeDiff);
            RecordTiming(TimingKind.Press, judge, judgeTime, targetNote);
            ApplyJudgment(targetNote, listIndex, judge);

        }

        /// <summary>
        /// 이번 입력이 어떤 판정이 될지 예측한다. 타격음과 캐릭터 연출의 단일 진실 공급원.
        /// activeNotes가 비어 있으면 ghostNotes 맨 앞을 본다 — ActivateGhostNotes가 첫 노트를
        /// activeNotes로 옮긴 직후 FlushBufferedInput을 부르므로 그게 곧 실제 판정 대상이다.
        /// 덕분에 마디 전환 선입력에서도 소리가 다음 마디까지 밀리지 않는다.
        /// 판정·점수는 전혀 건드리지 않는다.
        ///
        /// 반환값이 둘로 나뉘는 이유:
        ///  - sound는 낼 타격음 등급이다. HitTarget일 때만 재생하며, 헛침·홀드 본체는 Umm이 실리지만 소리는 내지 않는다.
        ///  - result는 캐릭터가 무엇을 연출할지 정하는 분류다. 셋을 구분해야 한다.
        ///    NoTarget  = 칠 노트가 없음 → Miss
        ///    HoldBody  = 큐 맨 앞이 홀드 본체(홀드 재그립) → 아무 연출도 하지 않음
        ///    HitTarget = 판정이 생김. 오차가 Kind~Umm 사이면 sound가 Umm이지만 이건 "진짜 Umm 히트"다
        ///  sound == Umm 하나로 헛침을 판별하면 마지막 경우와 구분되지 않아 Hit_umm 대신 Miss가 나간다.
        /// </summary>
        private (JudgeType sound, PressResult result) PredictInput(int listIndex, double judgeTime)
        {
            var lane = _lanes[listIndex];

            NoteController target = null;
            if (lane.activeNotes.Count > 0)
            {
                target = lane.activeNotes.Peek();
            }
            else if (lane.ghostNotes.Count > 0)
            {
                target = lane.ghostNotes.Peek();
            }

            // 칠 노트가 아예 없다 = 허공에 친 것
            if (target == null)
            {
                return (JudgeType.Umm, PressResult.NoTarget);
            }

            // 누르는 판정 대상이 아님(홀드 본체/릴리즈). 홀드를 놓쳤다 다시 잡는 정상 플레이에서 나온다.
            NoteType targetType = target.noteData.noteType;
            if (targetType != NoteType.Normal && targetType != NoteType.HoldStart)
            {
                return (JudgeType.Umm, PressResult.HoldBody);
            }

            double timeDiff = Math.Abs(judgeTime - target.noteData.time);
            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖 = 헛침
            {
                return (JudgeType.Umm, PressResult.NoTarget);
            }

            return (GetJudgeType(timeDiff), PressResult.HitTarget);
        }

        /// <summary>
        /// 판정 등급에 해당하는 타격음을 재생한다. 원샷 재생기가 없으면 조용히 무시한다. [AUDIO-IP:C8]
        /// </summary>
        private void PlayHitSound(JudgeType type)
        {
            if (_oneShots == null) return;
            _oneShots.Play(_hitSounds[(int)type]);
        }

        // [AUDIO-IP:C9] 판정 확정 지점(Press/HoldBody/Release/Miss)에서 부른다. 오차는 + = 늦음.
        private void RecordTiming(TimingKind kind, JudgeType judge, double judgeTime, NoteController note)
        {
            if (_timingLog == null) return;
            _timingLog.Record(kind, (int)judge, (judgeTime - note.noteData.time) * 1000.0);
        }

        /// <summary>
        /// 선입력 버퍼를 소비하여 TryJudgeInput을 재호출.
        /// press → release → barStart 케이스: isLaneHolding이 false이면 버퍼 폐기 (phantom 홀딩 방지).
        /// </summary>
        private void FlushBufferedInput(int listIndex)
        {
            if (!_lanes[listIndex].bufferedInput.HasValue) return;

            double inputTime = _lanes[listIndex].bufferedInput.Value;
            _lanes[listIndex].bufferedInput = null;

            if (!_lanes[listIndex].isHolding) return; // 이미 손을 뗀 경우 폐기

            // 선입력이 버퍼링되던 시점에 예측 판정으로 이미 타격음과 캐릭터 연출을 냈으므로 여기선 억제
            // (한 입력 = 한 소리 = 한 연출)
            TryJudgeInput(listIndex + 1, inputTime, isFreshPress: false);
        }

        /// <summary>
        /// 홀드 중(isHolding)인 레인에 대해 매 프레임 호출. 맨 앞 노트가 Holding/HoldEnd이고
        /// Perfect 윈도우 안이면 "누르고 있음"만으로 자동 Perfect 판정한다(떼는 판정 아님).
        /// </summary>
        private void CheckHoldingBody(int listIndex, double judgeTime)
        {
            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0) return;

            //Debug.Log($"Lane {listIndex+1} Holding now, currentTime: {currentTime}");

            NoteController targetNote = queue.Peek();
            // HoldEnd도 Holding과 동일하게 누르고 있는지 판정
            if (targetNote.noteData.noteType != NoteType.Holding &&
                targetNote.noteData.noteType != NoteType.HoldEnd) return;

            // [AUDIO-IP:C5] 현재 시각을 다시 읽지 않고 이번 진행의 판정 시각을 쓴다
            double timeDiff = Math.Abs(judgeTime - targetNote.noteData.time);

            if (timeDiff < JUDGE_PERFECT)
            {
                targetNote.OnHit();
                RecordTiming(TimingKind.HoldBody, JudgeType.Perfect, judgeTime, targetNote);
                ApplyJudgment(targetNote, listIndex, JudgeType.Perfect);
            }

        }


        /// <summary>
        /// 키를 뗐을 때 호출. 홀드 상태를 해제하고, 맨 앞 노트가 HoldRelease면 떼는 타이밍을 윈도우로 판정한다.
        /// (HoldRelease가 아니면 릴리즈 판정 없이 상태 해제만)
        /// applyJudgement = false: 실제로 뗀 것이 아닌 release(ESC·입력 맵 끄기·포커스 상실로 생긴 합성 release,
        /// 곡 시계가 멈춘 동안의 release)라 상태만 해제하고 판정하지 않는다. [AUDIO-IP:C6]
        /// </summary>
        public void TryJudgeRelease(int laneIndex, double judgeTime, bool applyJudgement = true)
        {
            int listIndex = laneIndex - 1;

            // 키를 뗀 것은 홀드가 끝나는 세 원인 중 하나다. 판정 성공 여부와 무관하게 상태를 해제한다.
            StopHold(listIndex);
            if (!applyJudgement) return;

            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0) return;

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.HoldRelease) return; // 릴리즈 판정 노트가 없으면 무시

            double timeDiff = Math.Abs(judgeTime - targetNote.noteData.time);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                //Debug.Log("판정 범위 밖 입력");
                return;
            }

            JudgeType judge = GetJudgeType(timeDiff);
            RecordTiming(TimingKind.Release, judge, judgeTime, targetNote);

            // 떼는 판정도 플레이어 입력이므로 타격음을 낸다. 단 실패했을 때는 내지 않는다 —
            // 이 함수는 모든 키 릴리즈마다 불리므로, 일반 노트를 칠 때마다 소리가 두 번 나게 된다.
            PlayHitSound(judge);

            ApplyJudgment(targetNote, listIndex, judge);
        }

        // 타이밍 오차(절댓값, 초)를 판정 등급으로 매핑. 윈도우 상수는 Constants.cs
        private static JudgeType GetJudgeType(double timeDiff)
        {
            if (timeDiff <= JUDGE_PERFECT) return JudgeType.Perfect;
            if (timeDiff <= JUDGE_MASTER)  return JudgeType.Master;
            if (timeDiff <= JUDGE_IDEAL)   return JudgeType.Ideal;
            if (timeDiff <= JUDGE_KIND)    return JudgeType.Kind;
            return JudgeType.Umm;
        }


        private NotePosition GetNotePosition(int listIndex)
        {
            // 각 그룹 내 첫 번째 레인(짝수 인덱스) = Top, 두 번째(홀수) = Bottom
            return listIndex % 2 == 0 ? NotePosition.Top : NotePosition.Bottom;
        }

        /// <summary>
        /// 판정 확정 공통 처리. 노트를 activeNotes에서 제거하고 OnHit → GameManager로 판정/홀드 콜백 발화 → 이펙트 출력.
        /// GameManager 콜백이 ScoreManager·CharacterAnimator로 전파된다.
        /// </summary>
        private void ApplyJudgment(NoteController targetNote, int listIndex, JudgeType type)
        {
            //Debug.Log($"Note Judged: {type}");
            _lanes[listIndex].activeNotes.Dequeue();
            targetNote.OnHit();

            NotePosition pos = GetNotePosition(listIndex);
            int groupID = GetTrackGroupID(listIndex);
            gameManager.OnNoteJudged(type, pos, groupID);

            // 홀드 관련 이벤트 발화
            // - HoldStart(2) / Holding(3): 홀드 진입/유지 (중간 진입도 허용)
            // - HoldEnd(4): 홀드 본체 완주 → 홀드 종료
            // HoldRelease(5)는 여기서 다루지 않는다. 이 타입이 ApplyJudgment에 도달하는 경로는
            // TryJudgeRelease 하나뿐이고, 그 진입부가 큐를 보기 전에 이미 StopHold를 불렀다.
            var nt = targetNote.noteData.noteType;
            if (nt == NoteType.HoldStart || nt == NoteType.Holding)
                gameManager.OnHoldStart(pos, groupID);
            else if (nt == NoteType.HoldEnd)
                StopHold(listIndex);

            EffectJudgement(type, targetNote);
        }

        /// <summary>
        /// 이 레인의 홀드를 끝낸다. 키를 뗐거나, 본체를 완주했거나, 본체를 놓쳤을 때 호출한다.
        ///
        /// isHolding을 함께 내리는 것이 중요하다. 이게 남아 있으면 CheckHoldingBody가 계속 돌아
        /// 손을 떼지 않은 플레이어에게 다음 홀드의 본체가 키 입력 없이 공짜로 판정된다.
        /// 같은 레인에서 홀드가 끝난 뒤 다시 시작되는 패턴은 채보마다 수십 쌍씩 있다.
        /// </summary>
        private void StopHold(int listIndex)
        {
            _lanes[listIndex].isHolding = false;
            gameManager.OnHoldStop(GetNotePosition(listIndex), GetTrackGroupID(listIndex));
        }
        
        /// <summary>
        /// 맨 앞 노트가 Umm 윈도우(+JUDGE_UMM)까지 지나도록 판정되지 않았으면 miss 처리(Umm).
        /// SyncTime에서 레인마다 매 프레임 호출.
        /// </summary>
        private void CheckMissedNotes(int listIndex, double judgeTime)
        {
            if (_lanes[listIndex].activeNotes.Count == 0) return;

            NoteController targetNote = _lanes[listIndex].activeNotes.Peek();

            // 판정 싱크가 적용된 판정 시각으로 비교한다(치는 판정 창과 miss 컷오프가 함께 움직인다)
            if (judgeTime > targetNote.noteData.time + JUDGE_UMM)
            {
                RecordTiming(TimingKind.Miss, JudgeType.Umm, judgeTime, targetNote);
                _lanes[listIndex].activeNotes.Dequeue();
                targetNote.OnMiss();

                gameManager.OnNoteMissed();

                // 홀드의 끝점을 놓쳤으면 그 홀드는 여기서 끝난 것이다.
                // 특히 HoldRelease를 놓치는 건(제때 못 뗌) 플레이 중 흔한 실패인데,
                // 이 처리가 없으면 캐릭터가 키를 뗄 때까지 Hold 포즈에 갇힌다.
                // Holding(3)은 제외한다. 본체는 계속되고 끝점이 나중에 오기 때문이다.
                // 빽빽한 본체가 프레임 누락으로 미스되는 건 흔한데 거기서 끊으면 홀드가 수시로 깨진다.
                var nt = targetNote.noteData.noteType;
                if (nt == NoteType.HoldEnd || nt == NoteType.HoldRelease)
                    StopHold(listIndex);

                EffectJudgement(JudgeType.Umm, targetNote);
            }
        }

        #endregion

        // 판정 이펙트를 풀에서 꺼내 노트 위치에 세팅. 재생이 끝나면 콜백으로 풀에 반환
        private void EffectJudgement(JudgeType type, NoteController targetNote)
        {
            GameObject effect = GetEffectFromPool();
            effect.GetComponent<EffectController>().Setup(type,
                targetNote.GetComponent<RectTransform>().anchoredPosition, (returnedEffect) => { ReturnEffectToPool(returnedEffect.gameObject); });
        }

        // 노트/홀드바/이펙트/판정선 공용 오브젝트 풀. 여유분이 있으면 재사용, 없으면 Instantiate. 반환 시 비활성화 후 재적재
        #region ObjectPooling
        private GameObject GetFromPool(Queue<GameObject> pool, GameObject prefab)
        {
            GameObject obj;
            if (pool.Count > 0)
            {
                obj = pool.Dequeue();
            }
            else
            {
                obj = Instantiate(prefab, objectPoolParent);
            }

            return obj;
        }
        
        private void ReturnToPool(Queue<GameObject> pool, GameObject go)
        {
            go.SetActive(false);
            // worldPositionStays=false: true면 월드 행렬이 local 값에 구워져 프리팹 localScale이 오염된다.
            // 홀드바는 localScale(2배 제작 에셋 보정값)을 그대로 읽어 쓰므로 반드시 false여야 한다.
            go.transform.SetParent(objectPoolParent, false);
            pool.Enqueue(go);
        }

        public GameObject GetNoteFromPool()
        {
            GameObject go = GetFromPool(notePool, notePrefab);
            return go;
        }

        public void ReturnNoteToPool(GameObject go) => ReturnToPool(notePool, go);


        private GameObject GetEffectFromPool()
        {
            GameObject go = GetFromPool(effectPool, effectPrefab);
            return go;
        }

        public void ReturnEffectToPool(GameObject go) => ReturnToPool(effectPool, go);


        
        private TimelineController GetTimelineFromPool()
        {
            GameObject go = GetFromPool(timelinePool, timelinePrefab);
            return go.GetComponent<TimelineController>();
        }
        
        public void ReturnTimelineToPool(GameObject go) => ReturnToPool(timelinePool, go);

        #endregion

    }
}
