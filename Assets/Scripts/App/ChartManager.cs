using System;
using System.Collections.Generic;
using SCOdyssey.App;
using SCOdyssey.Core;
using TMPro;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // ── 코어 루프 (메서드 호출 순서) ──────────────────────────────────────────
    //
    //  게임 시작 시 Init()을 1회 호출한다.
    //    PrepareNextBar() -> StartCurrentBar() -> StartMusic()
    //
    //  이후 매 프레임 GameManager.Update()가 SyncTime(time)을 호출하고,
    //  SyncTime()은 아래를 순서대로 수행한다.
    //    1) 현재 시간이 마디 종료 시각을 넘었으면  StartCurrentBar() -> CheckGameClear()
    //    2) JudgeTrack.Tick() -> 나온 JudgeEvent를 DispatchJudge()로 흘림 (miss 확정 + 홀드 자동판정)
    //    3) UpdateCountdowns()
    //
    //  키 입력은 프레임 루프와 별개로 들어온다.
    //    누르면 TryJudgeInput(), 떼면 TryJudgeRelease() 가 호출되고,
    //    판정이 잡히면 DispatchJudge() -> ApplyJudgement() 로 마무리한다.
    //
    //  판정 권한은 JudgeTrack(순수 데이터)에 있고, 여기는 그 결과를 뷰/점수/연출로 옮기는 역할만 한다.
    //  뷰 연결은 noteId(= NoteData.id = 판정 트랙 인덱스) -> _noteViews 로 되찾는다.
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

        private bool m_showPerfect;


        [Header("노트 풀링")]
        public GameObject notePrefab;
        private Queue<GameObject> notePool = new Queue<GameObject>();

        [Header("레이어 분리")]
        public RectTransform holdLayer;     // HoldBar용 Canvas (Inspector 할당)
        public RectTransform headLayer;     // NoteHead용 Canvas (Inspector 할당)
        public GameObject holdBarPrefab;    // holdImage만 있는 별도 프리팹 (Inspector 할당)
        private Queue<GameObject> holdBarPool = new Queue<GameObject>();

        [Header("이펙트 풀링")]
        public GameObject effectPrefab;
        private Queue<GameObject> effectPool = new Queue<GameObject>();


        [Header("판정선")]
        public GameObject timelinePrefab; // 판정선 프리팹
        private Queue<GameObject> timelinePool = new Queue<GameObject>();
        public RectTransform[] timelineTransforms = new RectTransform[LANE_GROUP_COUNT];   // 판정선의 상하 위치 좌표
        // 판정선 상태 2단계: preloaded(다음 마디용으로 생성만 됨) → active(현재 마디에서 실제 이동 중). key = 그룹ID(0/1)
        private Dictionary<LaneGroup, TimelineController> activeTimelines = new();
        private Dictionary<LaneGroup, TimelineController> preloadedTimelines = new();



        [Header("레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)



        [Header("마디 진행 관리")]
        private Queue<LaneData> remainingChart; // chartData 내의 모든 LaneData를 복사하여 사용
        private Queue<LaneData> nextBarLanes = new Queue<LaneData>();   // 다음 스크롤을 준비 중인 마디의 LaneData 리스트

        private int currentBarNumber = 0;      // 현재 마디 인덱스(0부터). StartCurrentBar 승격 시 ++
        private double currentBarEndTime = 0f; // 현재 마디의 종료 시간. SyncTime에서 currentTime이 이 값을 넘으면 다음 마디로 전환
        private bool endOfChartLogged = false; // 채보 종료 로그 1회 제한 (StartCurrentBar가 매 프레임 재진입하므로)
        private double barDuration = 0f; // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM

        public TextMeshProUGUI[] countdownTexts = new TextMeshProUGUI[COUNTDOWN_SLOT_COUNT];

        private ChartState _chartState;

        private readonly JudgeTrack _judgeTrack = new();
        private readonly List<JudgeEvent> _judgeEvents = new();   // Tick 결과 수신용. 매 프레임 재사용

        // noteId -> 뷰. 판정 결과로 어떤 NoteController를 건드릴지 되찾는 통로.
        // 스폰 때 등록하고, 판정/miss로 꺼낼 때 해제한다.
        private NoteController[] _noteViews = Array.Empty<NoteController>();

        // 다음 마디 준비 시 재사용하는 임시 버퍼(판정선 생성/재활용/제거 판단용 스크래치)
        // private readonly HashSet<int> _nextGroupsBuffer = new HashSet<int>();        // 다음 마디에 등장할 그룹 ID 집합
        private readonly Dictionary<LaneGroup, bool> _nextGroupDirBuffer = new(); // 그룹별 진행 방향(isLTR)


        void Awake()
        {
            _chartState = new();
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
            endOfChartLogged = false;   // 재시작 시 로그 1회 제한 초기화

            double judgementOffsetSec = 0;

            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
            {
                m_showPerfect = settingsManager.Current.showPerfect;
                judgementOffsetSec = settingsManager.Current.judgmentOffset * 0.003;
            }

            JudgeNote[] judgeNotes = chartData.BuildJudgeTrack();
            _judgeTrack.Init(judgeNotes, judgementOffsetSec);
            _noteViews = new NoteController[judgeNotes.Length];
            LogJudgeTrackSummary(chartData, judgeNotes);

            // TODO: 4/4박자가 아닐경우의 barDuration 계산 (BPM 기반)
            barDuration = 60f / chartData.bpm * 4f; // 4/4박자 기준
            currentBarEndTime = 0f + barDuration;

            _chartState.Init();

            for (int i = 0; i < COUNTDOWN_SLOT_COUNT; i++)
            {
                countdownTexts[i].gameObject.SetActive(false);
                countdownTexts[i].text = "";
            }

            PrepareNextBar();   // 0번(빈) 마디 이후 첫 마디를 미리 준비
            StartCurrentBar();  // 준비된 마디를 현재 마디로 승격 + 다음 마디 선행 준비

            // barDuration만큼 음원 재생을 지연 → 0번 빈 마디가 흐르는 동안 1번 마디를 준비할 시간을 확보
            gameManager.StartMusic(barDuration);
        }

        // flat 트랙 대조용 임시 메서드
        private void LogJudgeTrackSummary(ChartData chartData, JudgeNote[] judgeNotes)
        {
            int viewNoteCount = 0;
            foreach (LaneData lane in chartData.GetFullChartList())
                viewNoteCount += lane.Notes.Count;

            // 시간 오름차순인지 확인
            int unsortedAt = -1;
            for (int i = 1; i < judgeNotes.Length; i++)
            {
                if (judgeNotes[i - 1].Time > judgeNotes[i].Time)
                {
                    unsortedAt = i;
                    break;
                }
            }

            var kindCount = new int[(int)NoteType.HoldRelease + 1];
            foreach (JudgeNote note in judgeNotes) kindCount[(int)note.Kind]++;

            Debug.Log(
                $"[JudgeTrack] {judgeNotes.Length}개 (뷰 경로 {viewNoteCount}개 / 헤더 #NOTES {chartData.totalNotes})\n" +
                $"  정렬: {(unsortedAt < 0 ? "OK" : $"깨짐! index {unsortedAt}")}\n" +
                $"  타입: Normal={kindCount[(int)NoteType.Normal]}, " +
                $"HoldStart={kindCount[(int)NoteType.HoldStart]}, " +
                $"Holding={kindCount[(int)NoteType.Holding]}, " +
                $"HoldEnd={kindCount[(int)NoteType.HoldEnd]}, " +
                $"HoldRelease={kindCount[(int)NoteType.HoldRelease]}");
        }

        /// <summary>
        /// 매 프레임 GameManager가 현재 게임 시간을 주입하는 진입점.
        /// 마디 종료 시각을 넘으면 다음 마디로 전환하고, 레인별 miss/holding 판정과 카운트다운을 갱신한다.
        /// </summary>
        public void SyncTime(double time)
        {
            // 현재 마디 종료 시각을 넘어서면 다음 마디로 전환하고 종료 조건도 확인
            if (time >= this.currentBarEndTime)
            {
                this.StartCurrentBar();
                this.CheckGameClear();
            }

            _judgeEvents.Clear();
            _judgeTrack.Tick(time, _judgeEvents);
            foreach (JudgeEvent judged in _judgeEvents) DispatchJudge(judged);

            /// 매 프레임 호출. 활성 카운트다운 레인에 대해 다음 마디 시작까지 남은 ¼마디 비트 수를 3/2/1로 표시.
            /// 목표 시각에 도달하면(남은 시간 &lt;= 0) 텍스트를 끄고 비활성화한다.
            double beatDuration = this.barDuration / 4.0d;   // ¼마디 = 카운트다운 1칸

            _chartState.UpdateCountdowns(
                currentTime: time,
                onTimeDiffMinus: (slot) => this.countdownTexts[(int)slot].gameObject.SetActive(false),
                onUpdateRemaining: (slot, timeDiff) =>
                {
                    double remainingBeats = timeDiff / beatDuration;

                    if (remainingBeats <= 3.01d)
                    {
                        int displayNum = (int)Math.Ceiling(remainingBeats);

                        if (displayNum > 0 && displayNum <= 3)
                        {
                            this.countdownTexts[(int)slot].text = displayNum.ToString();
                        }
                    }
                    else
                    {
                        this.countdownTexts[(int)slot].text = "";
                    }
                }
            );
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

            if (!_judgeTrack.IsFinished) return;

            // 음악이 아직 재생 중이면 대기
            if (gameManager.IsAudioPlaying) return;

            Debug.Log("Game Cleared.");
            gameManager.OnGameFinished();
        }


        #region Bar
        /// <summary>
        /// 다음 마디를 "준비만" 한다(아직 현재 마디로 만들지 않음).
        /// remainingChart에서 다음 마디에 해당하는 LaneData를 모두 nextBarLanes로 옮긴 뒤
        /// 판정선을 프리로드하고 노트를 Ghost/Hidden 상태로 미리 스폰한다.
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

            if (nextBarLanes.Count > 0)
            {
                PreloadTimelines();  // 이 마디에 필요한 판정선 생성/재활용 준비
                SpawnNextNotes();    // 노트 오브젝트를 Ghost/Hidden으로 미리 스폰
            }

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
                if (!endOfChartLogged)
                {
                    Debug.Log("End of Chart Reached.");
                    endOfChartLogged = true;
                }
                return;
            }

            // 2) 이번 마디에 등장할 그룹과 방향 수집
            _nextGroupDirBuffer.Clear();

            foreach (var lane in nextBarLanes)
            {
                var groupID = LaneMap.FromChartLine(lane.line).GetGroup();
                _nextGroupDirBuffer[groupID] = lane.isLTR;
            }

            // 3) 이미 이동 중인 판정선 처리: 같은 그룹인데 방향이 반대면 재활용(유턴), 그 외는 제거 목록에
            Span<bool> groupsToRemove = stackalloc bool[2]{false, false}; // GroupID는 0 or 1

            foreach (var (groupID, timeline) in activeTimelines)
            {
                if (_nextGroupDirBuffer.ContainsKey(groupID) && timeline.isLTR != _nextGroupDirBuffer[groupID])
                {
                    // 재활용: 풀에 반환하지 않고 방향을 뒤집어 새 마디로 다시 Init (캐릭터 중복 교차 방지)
                    bool isLTR = _nextGroupDirBuffer[groupID];
                    GetTimelinePositions(isLTR, out float startX, out float endX);

                    timeline.Init(
                        startTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); },
                        groupID: (int)groupID
                    );
                }
                else
                {
                    groupsToRemove[(int)groupID] = true;   // 이번 마디에 안 쓰거나 방향 동일 → activeTimelines에서 뺌
                }
            }

            for(int i=0; i<groupsToRemove.Length; ++i)
            {
                if(groupsToRemove[i]) activeTimelines.Remove((LaneGroup)i);
            }

            // 프리로드된 판정선을 activeTimelines로 승격(실제 이동 시작).
            foreach (var (groupID, timeline) in preloadedTimelines)
            {
                if (!activeTimelines.TryAdd(groupID, timeline))
                {
                    Debug.LogWarning("Timeline 승격 중복 발생: 그룹 " + groupID);
                    ReturnTimelineToPool(timeline.gameObject);
                }
            }

            preloadedTimelines.Clear();
            _chartState.ActivateGhostNotes(
                activeTimelines: activeTimelines
            );

            // 5) 마디 번호 증가 후, 그 다음 마디를 다시 선행 준비 (항상 한 마디 앞서 준비 유지)
            currentBarNumber++;
            PrepareNextBar();

        }
        #endregion


        #region Timeline

        /// <summary>
        /// 다음 마디에 필요한 판정선을 준비한다.
        /// 방향이 뒤집힌 기존 판정선이 있으면 재활용 대상으로 두어 새로 만들지 않고(재활용은 StartCurrentBar에서 수행),
        /// 없으면 풀에서 새 판정선을 화면 밖에 생성해 preloadedTimelines에 넣는다. 카운트다운도 함께 켠다.
        /// </summary>
        private void PreloadTimelines()
        {
            _nextGroupDirBuffer.Clear();

            foreach (var lane in nextBarLanes)
            {
                var groupID = LaneMap.FromChartLine(lane.line).GetGroup();
                _nextGroupDirBuffer[groupID] = lane.isLTR;
            }

            double nextStartTime = currentBarNumber * barDuration;

            foreach (var (groupID, isLTR) in _nextGroupDirBuffer)
            {
                // 이미 이동 중인 판정선이 방향만 반대면 재활용 대상 → 여기서는 새로 만들지 않음
                bool isReused = false;
                if (activeTimelines.TryGetValue(groupID, out var existing) &&
                    existing.isLTR != isLTR)
                {
                    isReused = true;
                }

                if (!isReused)  // 새 판정선 생성
                {
                    TimelineController timeline = GetTimelineFromPool();
                    timeline.transform.SetParent(timelineParent, false);
                    timeline.transform.position = timelineTransforms[(int)groupID].position;

                    GetTimelinePositions(isLTR, out float startX, out float endX);

                    timeline.Init(
                        nextStartTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); },
                        groupID: (int)groupID
                    );

                    preloadedTimelines.Add(groupID, timeline);
                }

                // 카운트다운은 방향에 따라 좌/우 슬롯이 달라짐
                CountdownSlot slot = groupID.ToCountdownSlot(isLTR);

                _chartState.ActivateCountdown(
                    slot: slot,
                    targetTime: nextStartTime
                );

                countdownTexts[(int)slot].gameObject.SetActive(true);
                countdownTexts[(int)slot].text = "";
            }

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
        /// nextBarLanes의 각 노트를 풀에서 꺼내 위치를 계산해 배치하고, 초기 상태(Ghost/Hidden)를 결정한 뒤
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
                RectTransform laneRT = laneTransforms[(int)LaneMap.FromChartLine(lane.line)];
                // 노트 배치 시작점 x좌표 위치
                float laneStartX = lane.isLTR ? leftEndpoint.anchoredPosition.x : rightEndpoint.anchoredPosition.x;

                var groupID = LaneMap.FromChartLine(lane.line).GetGroup();

                // 충돌 = 현재 이동 중인 판정선과 같은 그룹을 다음 마디에서도 사용하는 경우(고난이도).
                // 이때 다음 마디 노트를 그냥 Ghost로 띄우면 현재 판정선과 겹쳐 난잡 → Hidden으로 숨겼다가 판정선이 지난 뒤 Ghost로 전환.
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
                    _chartState.EnqueueGhostNotes(LaneMap.FromChartLine(lane.line), noteController);

                    if (noteData.id >= 0 && noteData.id < _noteViews.Length)
                        _noteViews[noteData.id] = noteController;

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
                        // 같은 레인 충돌: 노트가 현재 타임라인의 endpoint에 위치하는지 확인
                        // endpoint 노트는 판정선이 절대 지나칠 수 없어 Hidden 유지 시 Active로 직행하므로 즉시 Ghost 표시
                        float noteX = spawnPos.x;
                        bool atEndpoint = currentIsLTR
                            ? Mathf.Approximately(noteX, rightEndpoint.anchoredPosition.x)  // LTR: rightEndpoint
                            : Mathf.Approximately(noteX, leftEndpoint.anchoredPosition.x);  // RTL: leftEndpoint

                        if (!atEndpoint)
                        {
                            // endpoint가 아님: 판정선 감시로 Ghost 전환
                            noteController.TrackTimeline(currentTimeline);
                            noteController.SetState(NoteState.Hidden);
                        }
                        else
                        {
                            // endpoint에 위치: 즉시 Ghost로 표시
                            noteController.SetState(NoteState.Ghost);
                        }
                    }
                    else
                    {
                        // 충돌 없음: 바로 반투명 노출
                        noteController.SetState(NoteState.Ghost);
                    }
                }
            }
        }

        #endregion


        #region Judgement
        /// <summary>
        /// 키를 눌렀을 때 호출(GameManager가 라우팅). Normal/HoldStart만 눌러서 판정한다.
        /// (홀드 본체는 Tick의 자동 판정, 릴리즈는 TryJudgeRelease 담당)
        /// </summary>
        public void TryJudgeInput(Lane lane, double inputGameTime)
        {
            if (_judgeTrack.TryPress(lane, inputGameTime, out JudgeEvent judged))
                DispatchJudge(judged);
        }

        /// <summary>
        /// 키를 뗐을 때 호출. 홀드 상태를 해제하고, 윈도우 안에 HoldRelease가 있으면 떼는 타이밍을 판정한다.
        /// </summary>
        public void TryJudgeRelease(Lane lane, double inputGameTime)
        {
            if (_judgeTrack.TryRelease(lane, inputGameTime, out JudgeEvent judged))
                DispatchJudge(judged);
        }

        // 판정된 노트의 뷰를 꺼내면서 등록 해제. 같은 노트를 두 번 건드리지 않게 한다.
        private NoteController TakeNoteView(int noteId)
        {
            if (noteId < 0 || noteId >= _noteViews.Length) return null;

            NoteController view = _noteViews[noteId];
            _noteViews[noteId] = null;
            return view;
        }

        // JudgeTrack이 확정한 판정 1건을 뷰/점수/연출로 흘려보낸다.
        private void DispatchJudge(JudgeEvent judged)
        {
            NoteController view = TakeNoteView(judged.NoteId);

            if (judged.IsMiss)
            {
                view?.OnMiss();
                gameManager.OnNoteMissed();
                if (view != null) EffectJudgement(JudgeType.Umm, view);
                return;
            }

            view?.OnHit();
            ApplyJudgement(judged, view);
        }



        private static NotePosition GetNotePosition(int listIndex)
        {
            // 각 그룹 내 첫 번째 레인(짝수 인덱스) = Top, 두 번째(홀수) = Bottom
            return listIndex % 2 == 0 ? NotePosition.Top : NotePosition.Bottom;
        }

        /// <summary>
        /// 판정 확정 공통 처리. 노트를 activeNotes에서 제거하고 OnHit → GameManager로 판정/홀드 콜백 발화 → 이펙트 출력.
        /// GameManager 콜백이 ScoreManager·CharacterAnimator로 전파된다.
        /// </summary>
        private void ApplyJudgement(JudgeEvent judged, NoteController view)
        {
            var listIndex = (int)judged.Lane;
            NotePosition pos = GetNotePosition(listIndex);
            int groupID = (int)judged.Lane.GetGroup();
            gameManager.OnNoteJudged(judged.Judge, pos, groupID);

            // 홀드 관련 이벤트 발화
            // - HoldStart(2) / Holding(3): 홀드 진입/유지 (중간 진입도 허용)
            // - HoldEnd(4): 홀드 본체 완주 (성공 피드백)
            // - HoldRelease(5): 릴리즈 판정 (홀드 상태 해제)
            if (judged.Kind == NoteType.HoldStart || judged.Kind == NoteType.Holding)
                gameManager.OnHoldStart(pos, groupID);
            else if (judged.Kind == NoteType.HoldEnd)
                gameManager.OnHoldEnd(pos, groupID);
            else if (judged.Kind == NoteType.HoldRelease)
                gameManager.OnHoldRelease(pos, groupID);

            if (view == null) return;   // 스폰 전이거나 이미 회수된 노트. 점수/이벤트는 위에서 이미 나갔다

            if (!m_showPerfect && judged.Judge == JudgeType.Perfect)
                EffectJudgement(JudgeType.Master, view);
            else
                EffectJudgement(judged.Judge, view);
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
            go.transform.SetParent(objectPoolParent);
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
