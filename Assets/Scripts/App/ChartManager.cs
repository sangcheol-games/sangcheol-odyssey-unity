using System.Collections.Generic;
using SCOdyssey.App;
using UnityEngine;

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

            for (int i = 0; i < 4; i++) activeNotes[i] = new Queue<NoteController>();

            PrepareNextBar();
            StartCurrentBar();
        }

        public void SyncTime(float time)
        {
            this.currentTime = time;
            
            if (remainingChart.Count > 0 && currentTime >= currentBarEndTime)
            {
                //Debug.Log("currentTime:" + currentTime);
                StartCurrentBar();
            }
        }



        private void PrepareNextBar()
        {
            nextBarLanes.Clear();
            if (remainingChart.Count == 0)
            {
                Debug.Log("End of Chart.");
                return;
            }

            int nextBar = currentBarNumber + 1;
            //Debug.Log("Preparing Bar " + nextBar);

            while (true)    // 다음 마디에 해당하는 모든 LaneData를 remaingChart => nextBarLanes로 이동
            {
                LaneData nextLane = remainingChart.Peek();
                if (nextLane.bar > nextBar) break;

                nextBarLanes.Enqueue(nextLane);
                remainingChart.Dequeue();
            }

            //Debug.Log($"Prepared Bar {nextBar} with {nextBarLanes.Count} lanes.");

            // TODO 다음 마디를 반투명하게 씬에 시각화
        }

        private void StartCurrentBar()
        {
            float startTime = currentBarNumber * barDuration;
            currentBarEndTime = startTime + barDuration;
            //Debug.Log($"Starting Bar {currentBarNumber + 1} at time {currentTime}, ends at {currentBarEndTime}");

            if (nextBarLanes.Count == 0)
            {
                // TODO: 게임 종료 처리
                Debug.Log("Game Cleared.");
                return;
            }

            currentBarLanes = new List<LaneData>(nextBarLanes);

            HashSet<int> currentGroups = new HashSet<int>();
            Dictionary<int, bool> groupDirection = new Dictionary<int, bool>();

            foreach (var laneData in currentBarLanes)
            {
                int groupID = GetTrackGroupID(laneData.line - 1);
                if (!currentGroups.Contains(groupID))
                {
                    currentGroups.Add(groupID);
                    groupDirection[groupID] = laneData.isLTR;
                }

            }
            
            foreach (int groupID in currentGroups)
            {
                SpawnTimelines(startTime, groupID, groupDirection[groupID]);
            }


            SpawnNotes(currentBarLanes, startTime);
            currentBarNumber++;
            PrepareNextBar();

        }
        private int GetTrackGroupID(int laneIndex)
        {
            return laneIndex <= 1 ? 0 : 1;
        }

        #region Timeline

        private void SpawnTimelines(float startTime, int groupID, bool isLTR)
        {
            TimelineController timeline = null;

            if (activeTimelines.TryGetValue(groupID, out TimelineController existingTimeline))
            {
                timeline = existingTimeline;
                activeTimelines.Remove(groupID);
            }
            else
            {
                timeline = GetTimelineFromPool();
                timeline.transform.SetParent(timelineParent, false);
                timeline.transform.position = timelineTransforms[groupID].position;
            }

            float startX, endX;
            GetTimelinePositions(isLTR, out startX, out endX);
            timeline.Init(startTime, barDuration, startX, endX, (timeline) => { ReturnTimelineToPool(timeline.gameObject); });

            activeTimelines[groupID] = timeline;
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




        private void SpawnNotes(List<LaneData> lanesToSpawn, float startTime)
        {
            foreach (var lane in lanesToSpawn)
            {
                float laneWidth = rightEndpoint.anchoredPosition.x - leftEndpoint.anchoredPosition.x;
                //Debug.Log($"Lane {lane.line} Width: {laneWidth}");
                float noteInterval = laneWidth / lane.beat;
                //Debug.Log($"Note Interval: {noteInterval}");

                // 레인 y좌표 기준점 획득
                RectTransform laneRT = laneTransforms[lane.line - 1];
                // 노트 배치 시작점 x좌표 위치
                float laneStartX = lane.isLTR ? leftEndpoint.anchoredPosition.x : rightEndpoint.anchoredPosition.x;
                //Debug.Log($"Lane {lane.line} Start X: {laneStartX}");

                foreach (var noteData in lane.Notes)
                {
                    GameObject note = GetNoteFromPool();
                    //if(note == null) Debug.LogError("노트 풀에서 오브젝트를 가져오지 못했습니다.");
                    NoteController noteController = note.GetComponent<NoteController>();
                    //if(noteController == null) Debug.LogError("노트 오브젝트에서 컴포넌트를 가져오지 못했습니다.");

                    note.transform.SetParent(noteParent, false);

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

                    activeNotes[lane.line - 1].Enqueue(noteController);
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
