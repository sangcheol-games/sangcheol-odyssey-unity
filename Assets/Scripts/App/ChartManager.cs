using System;
using System.Collections.Generic;
using SCOdyssey.App;
using SCOdyssey.Core;
using TMPro;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class ChartManager : MonoBehaviour
    {
        private IGameManager gameManager;

        public RectTransform noteParent;
        public RectTransform timelineParent;
        public Transform objectPoolParent;

        private double currentTime;

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
        public RectTransform[] timelineTransforms = new RectTransform[2];   // 판정선의 상하 위치 좌표
        private Dictionary<int, TimelineController> activeTimelines = new Dictionary<int, TimelineController>();
        private Dictionary<int, TimelineController> preloadedTimelines = new Dictionary<int, TimelineController>();



        [Header("레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)



        [Header("마디 진행 관리")]
        private Queue<LaneData> remainingChart; // chartData 내의 모든 LaneData를 복사하여 사용
        private List<LaneData> currentBarLanes = new List<LaneData>();  // 현재 스크롤 중인 마디의 LaneData 리스트
        private Queue<LaneData> nextBarLanes = new Queue<LaneData>();   // 다음 스크롤을 준비 중인 마디의 LaneData 리스트

        private int currentBarNumber = 0;
        private double currentBarEndTime = 0f; // 현재 마디의 종료 시간
        private double barDuration = 0f; // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM

        public TextMeshProUGUI[] countdownTexts = new TextMeshProUGUI[LANE_COUNT];

        private LaneState[] _lanes;

        private class LaneState
        {
            public readonly Queue<NoteController> activeNotes = new Queue<NoteController>();
            public readonly Queue<NoteController> ghostNotes = new Queue<NoteController>();
            public bool isHolding;
            public double? bufferedInput;
            public double countdownTargetTime;
            public bool isCountdownActive;
        }


        private Action<JudgeType, NoteController> judgeEffectAction;

        private double _judgmentOffsetSec;

        private readonly HashSet<int> _nextGroupsBuffer = new HashSet<int>();
        private readonly Dictionary<int, bool> _nextGroupDirBuffer = new Dictionary<int, bool>();
        private readonly List<int> _groupsToRemoveBuffer = new List<int>();


        void Awake()
        {
            _lanes = new LaneState[LANE_COUNT];
            for (int i = 0; i < LANE_COUNT; i++)
                _lanes[i] = new LaneState();
        }

        public void Init(ChartData chartData, IGameManager gameManager)
        {
            this.gameManager = gameManager;
            remainingChart = new Queue<LaneData>(chartData.GetFullChartList());
            currentBarNumber = 0;

            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
            {
                m_showPerfect = settingsManager.Current.showPerfect;
                _judgmentOffsetSec = settingsManager.Current.judgmentOffset * 0.003;
            }

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

                countdownTexts[i].gameObject.SetActive(false);
                countdownTexts[i].text = "";
            }

            PrepareNextBar();
            StartCurrentBar();

            gameManager.StartMusic(barDuration);
        }

        public void SyncTime(double time)
        {
            this.currentTime = time;

            if (remainingChart.Count >= 0 && currentTime >= currentBarEndTime)
            {
                StartCurrentBar();
                CheckGameClear();
            }

            for (int i = 0; i < LANE_COUNT; i++)
            {
                CheckMissedNotes(i, currentTime);
                if (_lanes[i].isHolding) CheckHoldingBody(i);
            }
            
            UpdateCountdowns();

        }
        
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
        private void PrepareNextBar()
        {
            nextBarLanes.Clear();

            int nextBar = currentBarNumber;

            while (remainingChart.Count > 0)    // 다음 마디에 해당하는 모든 LaneData를 remaingChart => nextBarLanes로 이동
            {
                LaneData nextLane = remainingChart.Peek();
                if (nextLane.bar > nextBar) break;

                nextBarLanes.Enqueue(nextLane);
                remainingChart.Dequeue();
            }

            if (nextBarLanes.Count > 0)
            {
                PreloadTimelines();
                SpawnNextNotes();
            }

        }

        private void StartCurrentBar()
        {
            double startTime = currentBarNumber * barDuration;
            currentBarEndTime = startTime + barDuration;

            if (nextBarLanes.Count == 0)
            {
                Debug.Log("End of Chart Reached.");
                return;
            }

            _nextGroupsBuffer.Clear();
            _nextGroupDirBuffer.Clear();

            foreach (var lane in nextBarLanes)
            {
                int groupID = GetTrackGroupID(lane.line - 1);
                _nextGroupsBuffer.Add(groupID);
                _nextGroupDirBuffer[groupID] = lane.isLTR;
            }

            _groupsToRemoveBuffer.Clear();

            foreach (var kvp in activeTimelines)
            {
                int groupID = kvp.Key;
                TimelineController timeline = kvp.Value;

                if (_nextGroupsBuffer.Contains(groupID) && timeline.isLTR != _nextGroupDirBuffer[groupID])
                {
                    // 재활용
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
                    _groupsToRemoveBuffer.Add(groupID);
                }
            }

            foreach (int id in _groupsToRemoveBuffer) activeTimelines.Remove(id);

            ActivateTimelines();
            ActivateGhostNotes();
            

            currentBarLanes = new List<LaneData>(nextBarLanes);

            currentBarNumber++;
            PrepareNextBar();

        }
        private int GetTrackGroupID(int laneIndex)
        {
            return laneIndex <= 1 ? 0 : 1;
        }

        #endregion


        private void UpdateCountdowns()
        {
            double beatDuration = barDuration / 4.0d; 

            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (!_lanes[i].isCountdownActive) continue;

                double timeDiff = _lanes[i].countdownTargetTime - currentTime;

                if (timeDiff <= 0)
                {
                    countdownTexts[i].gameObject.SetActive(false);
                    _lanes[i].isCountdownActive = false;
                    continue;
                }

                double remainingBeats = timeDiff / beatDuration;

                if (remainingBeats <= 3.01d) 
                {
                    int displayNum = (int)Math.Ceiling(remainingBeats);     // 올림 처리

                    if (displayNum > 0 && displayNum <= 3)
                    {
                        countdownTexts[i].text = displayNum.ToString();
                    }
                }
                else
                {
                    countdownTexts[i].text = "";
                }

            }
        }
        
        private void ActivateCountdown(int index, double targetTime)
        {
            if (_lanes[index].isCountdownActive && Math.Abs(_lanes[index].countdownTargetTime - targetTime) < 0.01d) return;

            countdownTexts[index].gameObject.SetActive(true);
            countdownTexts[index].text = "";

            _lanes[index].countdownTargetTime = targetTime;
            _lanes[index].isCountdownActive = true;
        }





        #region Timeline

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

                int uiIndex = (groupID * 2) + (_nextGroupDirBuffer[groupID] ? 0 : 1);
                ActivateCountdown(uiIndex, nextStartTime);
            }

        }

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
        private void SpawnNextNotes()
        {
            foreach (var lane in nextBarLanes)
            {
                float laneWidth = rightEndpoint.anchoredPosition.x - leftEndpoint.anchoredPosition.x;
                float noteInterval = laneWidth / lane.beat;

                // 레인 y좌표 기준점 획득
                RectTransform laneRT = laneTransforms[lane.line - 1];
                // 노트 배치 시작점 x좌표 위치
                float laneStartX = lane.isLTR ? leftEndpoint.anchoredPosition.x : rightEndpoint.anchoredPosition.x;

                int groupID = GetTrackGroupID(lane.line - 1);

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
                    note.transform.SetParent(headLayer, false);

                    NoteAdapter noteAdapter = note.GetComponent<NoteAdapter>();

                    NoteController noteController = noteAdapter.ActivateAndGet(noteData.noteType);

                    Vector2 spawnPos = new Vector2(
                        laneStartX + noteInterval * noteData.index * (lane.isLTR ? 1 : -1),
                        laneRT.anchoredPosition.y
                    );

                    // HoldStart는 holdBarBeats * noteInterval로 실제 홀드바 길이 계산
                    float holdWidth = noteData.noteType == NoteType.HoldStart
                        ? noteInterval * (noteData.holdBarBeats ?? 1)
                        : noteInterval;

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

                    _lanes[lane.line - 1].ghostNotes.Enqueue(noteController);
                }
            }
        }

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
        public void TryJudgeInput(int laneIndex, double inputGameTime)
        {
            int listIndex = laneIndex - 1;  // 인덱스 보정
            _lanes[listIndex].isHolding = true;

            // 판정 결과와 무관하게 입력 이벤트를 먼저 발화 (캐릭터 Y 이동 담당)
            gameManager.OnLaneInput(GetNotePosition(listIndex), GetTrackGroupID(listIndex));

            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0)
            {
                // 마디 전환 직전 선입력: 노트가 활성화되면 FlushBufferedInput에서 재판정
                _lanes[listIndex].bufferedInput = inputGameTime;
                return;
            }

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.Normal && targetNote.noteData.noteType != NoteType.HoldStart) return;

            // 판정 타이밍 오프셋 적용: 윈도우 중심을 noteTime + offsetSec으로 이동
            double timeDiff = Math.Abs(inputGameTime - targetNote.noteData.time - _judgmentOffsetSec);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                //Debug.Log("판정 범위 밖 입력");
                return;
            }

            ApplyJudgment(targetNote, listIndex, GetJudgeType(timeDiff));

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

            TryJudgeInput(listIndex + 1, inputTime);
        }

        private void CheckHoldingBody(int listIndex)
        {
            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0) return;

            //Debug.Log($"Lane {listIndex+1} Holding now, currentTime: {currentTime}");

            NoteController targetNote = queue.Peek();
            // HoldEnd도 Holding과 동일하게 누르고 있는지 판정
            if (targetNote.noteData.noteType != NoteType.Holding &&
                targetNote.noteData.noteType != NoteType.HoldEnd) return;

            double timeDiff = Math.Abs(gameManager.GetCurrentTime() - targetNote.noteData.time - _judgmentOffsetSec);

            if (timeDiff < JUDGE_PERFECT)
            {
                targetNote.OnHit();
                ApplyJudgment(targetNote, listIndex, JudgeType.Perfect);
            }

        }


        public void TryJudgeRelease(int laneIndex, double inputGameTime)
        {
            int listIndex = laneIndex - 1;
            _lanes[listIndex].isHolding = false;

            // 키 릴리즈는 판정 성공 여부와 무관하게 홀드 상태 해제 신호로 사용
            gameManager.OnHoldRelease(GetNotePosition(listIndex), GetTrackGroupID(listIndex));

            var queue = _lanes[listIndex].activeNotes;
            if (queue.Count == 0) return;

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.HoldRelease) return; // 릴리즈 판정 노트가 없으면 무시

            double timeDiff = Math.Abs(inputGameTime - targetNote.noteData.time - _judgmentOffsetSec);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                Debug.Log("판정 범위 밖 입력");
                return;
            }

            ApplyJudgment(targetNote, listIndex, GetJudgeType(timeDiff));
        }

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
            // - HoldEnd(4): 홀드 본체 완주 (성공 피드백)
            // - HoldRelease(5): 릴리즈 판정 (홀드 상태 해제)
            var nt = targetNote.noteData.noteType;
            if (nt == NoteType.HoldStart || nt == NoteType.Holding)
                gameManager.OnHoldStart(pos, groupID);
            else if (nt == NoteType.HoldEnd)
                gameManager.OnHoldEnd(pos, groupID);
            else if (nt == NoteType.HoldRelease)
                gameManager.OnHoldRelease(pos, groupID);

            if (!m_showPerfect && type == JudgeType.Perfect)
                EffectJudgement(JudgeType.Master, targetNote);
            else
                EffectJudgement(type, targetNote);
        }
        
        private void CheckMissedNotes(int listIndex, double currentTime)
        {
            if (_lanes[listIndex].activeNotes.Count == 0) return;

            NoteController targetNote = _lanes[listIndex].activeNotes.Peek();

            if (currentTime > targetNote.noteData.time + JUDGE_UMM)
            {
                _lanes[listIndex].activeNotes.Dequeue();
                targetNote.OnMiss();

                gameManager.OnNoteMissed();

                EffectJudgement(JudgeType.Umm, targetNote);
            }
        }

        #endregion

        private void EffectJudgement(JudgeType type, NoteController targetNote)
        {
            GameObject effect = GetEffectFromPool();
            effect.GetComponent<EffectController>().Setup(type,
                targetNote.GetComponent<RectTransform>().anchoredPosition, (returnedEffect) => { ReturnEffectToPool(returnedEffect.gameObject); });
        }

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
