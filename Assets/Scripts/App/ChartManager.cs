using System.Collections.Generic;
using SCOdyssey.App;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class ChartManager : MonoBehaviour
    {
        public RectTransform noteParent;
        public RectTransform timelineParent;
        public Transform objectPoolParent;

        private float currentTime;



        [Header("노트 풀링")]
        public GameObject notePrefab;
        private Queue<GameObject> notePool = new Queue<GameObject>();


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
        private float currentBarEndTime = 0f; // 현재 마디의 종료 시간
        private float barDuration = 0f; // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM

        private Queue<NoteController>[] activeNotes = new Queue<NoteController>[4]; // 각 레인별 활성화된 노트 큐
        private Queue<NoteController>[] ghostNotes = new Queue<NoteController>[4]; // 각 레인별 고스트 노트 큐



        void Awake()
        {
            //chartData = GameManager.chartData;
        }

        public void Init(ChartData chartData)
        {
            remainingChart = new Queue<LaneData>(chartData.GetFullChartList());
            currentBarNumber = 0;

            // TODO: 4/4박자가 아닐경우의 barDuration 계산 (BPM 기반)
            barDuration = 60f / chartData.bpm * 4f; // 4/4박자 기준
            currentBarEndTime = 0f + barDuration;

            for (int i = 0; i < 4; i++)
            {
                activeNotes[i] = new Queue<NoteController>();
                ghostNotes[i] = new Queue<NoteController>();
            }

            PrepareNextBar();
            StartCurrentBar();
            GameManager.Instance.StartMusic(barDuration);
        }

        public void SyncTime(float time)
        {
            this.currentTime = time;

            if (remainingChart.Count >= 0 && currentTime >= currentBarEndTime)
            {
                StartCurrentBar();
                CheckGameClear();
            }

        }
        
        private void CheckGameClear()
        {
            if (remainingChart.Count > 0) return;
            if (nextBarLanes.Count > 0) return;

            for (int i = 0; i < 4; i++)
            {
                if (activeNotes[i].Count > 0) return;
                if (ghostNotes[i].Count > 0) return;
            }

            Debug.Log("Game Cleared.");
            GameManager.Instance.OnGameFinished();
        }



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
            float startTime = currentBarNumber * barDuration;
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

            ActivateGhostNotes();
            ActivateTimelines();

            currentBarLanes = new List<LaneData>(nextBarLanes);

            currentBarNumber++;
            PrepareNextBar();

        }
        private int GetTrackGroupID(int laneIndex)
        {
            return laneIndex <= 1 ? 0 : 1;
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

            float nextStartTime = currentBarNumber * barDuration;

            foreach (int groupID in nextGroups)
            {
                if (activeTimelines.ContainsKey(groupID))   // 재사용의 경우
                {
                    if (activeTimelines[groupID].isLTR != nextGroupDirection[groupID]) continue; 
                }
                if (preloadedTimelines.ContainsKey(groupID)) continue;  // 중복 방지

                // 새 판정선 생성
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
                    note.transform.SetParent(noteParent, false);

                    NoteController noteController = note.GetComponent<NoteController>();

                    Vector2 spawnPos = new Vector2(
                        laneStartX + noteInterval * noteData.index * (lane.isLTR ? 1 : -1),
                        laneRT.anchoredPosition.y
                    );

                    noteController.Init(
                        noteData,
                        spawnPos,
                        (returnedNote) => { ReturnNoteToPool(returnedNote.gameObject); },
                        (missedNote) => { RegisterMiss(missedNote);/* TODO: Miss 처리 로직 */ }
                    );

                    if (isConflict && currentTimeline != null)
                    {
                        // 같은 레인 충돌: 숨겨진 상태로 생성하고 판정선 감시 붙임
                        noteController.TrackTimeline(currentTimeline);
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
                    
                    activeNotes[i].Enqueue(note);
                }
            }
        }


        #region Judgement
        public void TryJudgeInput(int laneIndex)
        {
            var queue = activeNotes[laneIndex - 1];
            if (queue.Count == 0) return;

            NoteController note = queue.Peek();
            float timeDiff = Mathf.Abs(note.noteData.time - GameManager.Instance.GetCurrentTime());

            if (timeDiff <= 0.3f) // 임시 판정 범위 0.3초
            {
                queue.Dequeue();
                GameManager.Instance.OnNoteJudged(timeDiff);
                note.DeleteNote();
            }
        }

        public void RegisterMiss(NoteController note)
        {
            GameManager.Instance.OnNoteMissed();
            note.DeleteNote();

            var queue = activeNotes[note.noteData.laneIndex - 1];
            if (queue.Count > 0 && queue.Peek() == note)
            {
                queue.Dequeue();
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


        
        private TimelineController GetTimelineFromPool()
        {
            GameObject go = GetFromPool(timelinePool, timelinePrefab);
            return go.GetComponent<TimelineController>();
        }
        
        public void ReturnTimelineToPool(GameObject go) => ReturnToPool(timelinePool, go);

        #endregion

    }
}
