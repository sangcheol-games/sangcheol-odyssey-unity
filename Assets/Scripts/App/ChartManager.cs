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
        public RectTransform[] laneTransforms = new RectTransform[4]; // 4개 레인의 기준 위치 (씬에 배치된 4개의 LaneObject 할당)



        [Header("마디 진행 관리")]
        private Queue<LaneData> remainingChart; // chartData 내의 모든 LaneData를 복사하여 사용
        private List<LaneData> currentBarLanes = new List<LaneData>();  // 현재 스크롤 중인 마디의 LaneData 리스트
        private Queue<LaneData> nextBarLanes = new Queue<LaneData>();   // 다음 스크롤을 준비 중인 마디의 LaneData 리스트

        private int currentBarNumber = 0;
        private double currentBarEndTime = 0f; // 현재 마디의 종료 시간
        private double barDuration = 0f; // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM

        private Queue<NoteController>[] activeNotes = new Queue<NoteController>[4]; // 각 레인별 활성화된 노트 큐
        private Queue<NoteController>[] ghostNotes = new Queue<NoteController>[4]; // 각 레인별 고스트 노트 큐

        private bool[] isLaneHolding = { false, false, false, false }; // 각 레인별 롱노트 홀딩 상태 추적
        private double?[] bufferedInput = new double?[4]; // 마디 전환 직전 선입력 버퍼 (index: laneIndex - 1)
        
        public TextMeshProUGUI[] countdownTexts = new TextMeshProUGUI[4];

        private double[] countdownTargetTimes = new double[4];
        private bool[] isCountdownActive = new bool[4];



        void Awake()
        {
            //chartData = GameManager.chartData;
        }

        public void Init(ChartData chartData, IGameManager gameManager)
        {
            this.gameManager = gameManager;
            remainingChart = new Queue<LaneData>(chartData.GetFullChartList());
            currentBarNumber = 0;

            // TODO: 4/4박자가 아닐경우의 barDuration 계산 (BPM 기반)
            barDuration = 60f / chartData.bpm * 4f; // 4/4박자 기준
            currentBarEndTime = 0f + barDuration;

            for (int i = 0; i < 4; i++)
            {
                activeNotes[i] = new Queue<NoteController>();
                ghostNotes[i] = new Queue<NoteController>();

                countdownTexts[i].gameObject.SetActive(false);
                isCountdownActive[i] = false;
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

            for (int i = 0; i < 4; i++)
            {
                CheckMissedNotes(i, currentTime);
                if (isLaneHolding[i]) CheckHoldingBody(i);
            }
            
            UpdateCountdowns();

        }
        
        private void CheckGameClear()
        {
            // 게임이 이미 종료되었으면 중복 호출 방지
            if (!gameManager.IsGameRunning) return;

            if (remainingChart.Count > 0) return;
            if (nextBarLanes.Count > 0) return;

            for (int i = 0; i < 4; i++)
            {
                if (activeNotes[i].Count > 0) return;
                if (ghostNotes[i].Count > 0) return;
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

            HashSet<int> nextGroups = new HashSet<int>();
            Dictionary<int, bool> nextGroupDirection = new Dictionary<int, bool>();

            foreach (var lane in nextBarLanes)
            {
                int groupID = GetTrackGroupID(lane.line - 1);
                nextGroups.Add(groupID);
                nextGroupDirection[groupID] = lane.isLTR;
            }

            List<int> groupsToRemove = new List<int>(); // 제거할 그룹 ID 목록

            foreach (var kvp in activeTimelines)
            {
                int groupID = kvp.Key;
                TimelineController timeline = kvp.Value;

                if (nextGroups.Contains(groupID) && timeline.isLTR != nextGroupDirection[groupID])
                {
                    // 재활용
                    bool isLTR = nextGroupDirection[groupID];
                    float startX, endX;
                    GetTimelinePositions(isLTR, out startX, out endX);

                    timeline.Init(
                        startTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); }
                    );
                }
                else
                {
                    groupsToRemove.Add(groupID);    // 여기서 바로 제거하면 컬렉션 변경 오류 발생
                }
            }

            foreach (int id in groupsToRemove) activeTimelines.Remove(id);  // 반복문 종료 후 제거

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

            for (int i = 0; i < 4; i++)
            {
                if (!isCountdownActive[i]) continue;

                double timeDiff = countdownTargetTimes[i] - currentTime;

                if (timeDiff <= 0)
                {
                    countdownTexts[i].gameObject.SetActive(false);
                    isCountdownActive[i] = false;
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
            if (isCountdownActive[index] && Math.Abs(countdownTargetTimes[index] - targetTime) < 0.01d) return;

            countdownTexts[index].gameObject.SetActive(true);
            countdownTexts[index].text = "";

            countdownTargetTimes[index] = targetTime;
            isCountdownActive[index] = true;
        }





        #region Timeline

        private void PreloadTimelines()
        {
            HashSet<int> nextGroups = new HashSet<int>();
            Dictionary<int, bool> nextGroupDirection = new Dictionary<int, bool>();

            foreach (var laneData in nextBarLanes)
            {
                int groupID = GetTrackGroupID(laneData.line - 1);
                if (!nextGroups.Contains(groupID))
                {
                    nextGroups.Add(groupID);
                    nextGroupDirection[groupID] = laneData.isLTR;
                }
            }

            double nextStartTime = currentBarNumber * barDuration;

            foreach (int groupID in nextGroups)
            {
                bool isReused = false;
                if (activeTimelines.ContainsKey(groupID))   // 재사용의 경우
                {
                    if (activeTimelines[groupID].isLTR != nextGroupDirection[groupID])
                        isReused = true;
                }

                if (!isReused)  // 새 판정선 생성
                {
                    TimelineController timeline = GetTimelineFromPool();
                    timeline.transform.SetParent(timelineParent, false);
                    timeline.transform.position = timelineTransforms[groupID].position;

                    bool isLTR = nextGroupDirection[groupID];
                    float startX, endX;
                    GetTimelinePositions(isLTR, out startX, out endX);

                    timeline.Init(
                        nextStartTime,
                        barDuration,
                        startX,
                        endX,
                        (timeline) => { ReturnTimelineToPool(timeline.gameObject); }
                    );

                    preloadedTimelines.Add(groupID, timeline);
                }

                int uiIndex = (groupID * 2) + (nextGroupDirection[groupID] ? 0 : 1);
                ActivateCountdown(uiIndex, nextStartTime);
            }

        }

        private void ActivateTimelines()
        {
            foreach (var kvp in preloadedTimelines)
            {
                int groupID = kvp.Key;
                TimelineController timeline = kvp.Value;
                
                if (!activeTimelines.ContainsKey(groupID))
                {
                    activeTimelines.Add(groupID, timeline);
                }
                else
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

                    ghostNotes[lane.line - 1].Enqueue(noteController);
                }
            }
        }

        private void ActivateGhostNotes()
        {
            if (ghostNotes == null) return;
            for (int i = 0; i < 4; i++)
            {
                while (ghostNotes[i].Count > 0)
                {
                    NoteController note = ghostNotes[i].Dequeue();
                    note.SetState(NoteState.Active);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    // Holding/HoldEnd는 비주얼 없으므로 추적 불필요
                    if (note.noteData.noteType == NoteType.HoldStart)
                    {
                        int groupID = GetTrackGroupID(i);
                        if (activeTimelines.ContainsKey(groupID))
                        {
                            note.TrackTimeline(activeTimelines[groupID]);
                        }
                    }

                    activeNotes[i].Enqueue(note);
                    FlushBufferedInput(i); // 선입력이 있으면 즉시 재판정
                }
            }
        }

        #endregion


        #region Judgement
        public void TryJudgeInput(int laneIndex, double inputGameTime)
        {
            int listIndex = laneIndex - 1;  // 인덱스 보정
            isLaneHolding[listIndex] = true;

            var queue = activeNotes[listIndex];
            if (queue.Count == 0)
            {
                // 마디 전환 직전 선입력: 노트가 활성화되면 FlushBufferedInput에서 재판정
                bufferedInput[listIndex] = inputGameTime;
                return;
            }

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.Normal && targetNote.noteData.noteType != NoteType.HoldStart) return;

            double offsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var sm))
                offsetSec = sm.Current.judgmentOffset * 0.003;

            // 판정 타이밍 오프셋 적용: 윈도우 중심을 noteTime + offsetSec으로 이동
            double timeDiff = Math.Abs(inputGameTime - targetNote.noteData.time - offsetSec);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                Debug.Log("판정 범위 밖 입력");
                return;
            }

            JudgeType result = JudgeType.Perfect;

            if (timeDiff <= JUDGE_PERFECT) result = JudgeType.Perfect;
            else if (timeDiff <= JUDGE_MASTER) result = JudgeType.Master;
            else if (timeDiff <= JUDGE_IDEAL) result = JudgeType.Ideal;
            else if (timeDiff <= JUDGE_KIND) result = JudgeType.Kind;
            else if (timeDiff <= JUDGE_UMM) result = JudgeType.Umm;

            ApplyJudgment(targetNote, listIndex, result);

        }

        /// <summary>
        /// 선입력 버퍼를 소비하여 TryJudgeInput을 재호출.
        /// press → release → barStart 케이스: isLaneHolding이 false이면 버퍼 폐기 (phantom 홀딩 방지).
        /// </summary>
        private void FlushBufferedInput(int listIndex)
        {
            if (!bufferedInput[listIndex].HasValue) return;

            double inputTime = bufferedInput[listIndex].Value;
            bufferedInput[listIndex] = null;

            if (!isLaneHolding[listIndex]) return; // 이미 손을 뗀 경우 폐기

            TryJudgeInput(listIndex + 1, inputTime);
        }

        private void CheckHoldingBody(int listIndex)
        {
            var queue = activeNotes[listIndex];
            if (queue.Count == 0) return;

            //Debug.Log($"Lane {listIndex+1} Holding now, currentTime: {currentTime}");

            NoteController targetNote = queue.Peek();
            // HoldEnd도 Holding과 동일하게 누르고 있는지 판정
            if (targetNote.noteData.noteType != NoteType.Holding &&
                targetNote.noteData.noteType != NoteType.HoldEnd) return;

            double offsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var sm))
                offsetSec = sm.Current.judgmentOffset * 0.003;

            double timeDiff = Math.Abs(gameManager.GetCurrentTime() - targetNote.noteData.time - offsetSec);

            if (timeDiff < JUDGE_PERFECT)
            {
                targetNote.OnHit();
                ApplyJudgment(targetNote, listIndex, JudgeType.Perfect);
            }

        }


        public void TryJudgeRelease(int laneIndex, double inputGameTime)
        {
            int listIndex = laneIndex - 1;
            isLaneHolding[listIndex] = false;

            var queue = activeNotes[listIndex];
            if (queue.Count == 0) return;

            NoteController targetNote = queue.Peek();
            if (targetNote.noteData.noteType != NoteType.HoldRelease) return; // 릴리즈 판정 노트가 없으면 무시

            double offsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var sm2))
                offsetSec = sm2.Current.judgmentOffset * 0.003;

            double timeDiff = Math.Abs(inputGameTime - targetNote.noteData.time - offsetSec);

            if (timeDiff > JUDGE_UMM)   // 판정 범위 밖
            {
                Debug.Log("판정 범위 밖 입력");
                return;
            }

            JudgeType result = JudgeType.Perfect;

            if (timeDiff <= JUDGE_PERFECT) result = JudgeType.Perfect;
            else if (timeDiff <= JUDGE_MASTER) result = JudgeType.Master;
            else if (timeDiff <= JUDGE_IDEAL) result = JudgeType.Ideal;
            else if (timeDiff <= JUDGE_KIND) result = JudgeType.Kind;
            else if (timeDiff <= JUDGE_UMM) result = JudgeType.Umm;

            ApplyJudgment(targetNote, listIndex, result);
        }


        private NotePosition GetNotePosition(int listIndex)
        {
            // listIndex 0,1 → group 0 (Bottom), listIndex 2,3 → group 1 (Top)
            return listIndex <= 1 ? NotePosition.Bottom : NotePosition.Top;
        }

        private void ApplyJudgment(NoteController targetNote, int listIndex, JudgeType type)
        {
            Debug.Log($"Note Judged: {type}");
            activeNotes[listIndex].Dequeue();
            targetNote.OnHit();

            NotePosition pos = GetNotePosition(listIndex);
            gameManager.OnNoteJudged(type, pos);

            // 홀드 시작/종료 이벤트 발화
            if (targetNote.noteData.noteType == NoteType.HoldStart)
                gameManager.OnHoldStart(pos);
            else if (targetNote.noteData.noteType == NoteType.HoldRelease)
                gameManager.OnHoldEnd(pos);

            GameObject effect = GetEffectFromPool();
            effect.GetComponent<EffectController>().Setup(type,
                targetNote.GetComponent<RectTransform>().anchoredPosition, (returnedEffect) => { ReturnEffectToPool(returnedEffect.gameObject); });

        }
        
        private void CheckMissedNotes(int listIndex, double currentTime)
        {
            if (activeNotes[listIndex].Count == 0) return;

            NoteController targetNote = activeNotes[listIndex].Peek();

            if (currentTime > targetNote.noteData.time + JUDGE_UMM)
            {
                activeNotes[listIndex].Dequeue();
                targetNote.OnMiss();

                gameManager.OnNoteMissed();

                GameObject effect = GetEffectFromPool();
                effect.GetComponent<EffectController>().Setup(JudgeType.Umm,
                    targetNote.GetComponent<RectTransform>().anchoredPosition, (returnedEffect) => { ReturnEffectToPool(returnedEffect.gameObject); });
            }
        }

        #endregion




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
