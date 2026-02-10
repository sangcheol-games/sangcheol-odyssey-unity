using System.Collections.Generic;
using SCOdyssey.ChartEditor.Data;
using SCOdyssey.Game;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.Preview
{
    /// <summary>
    /// 에디터 프리뷰 재생 엔진.
    /// TimelineController + NotePrefab을 재활용하여 판정 없이 시각적 프리뷰 제공.
    /// </summary>
    public class EditorPreviewManager : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        // 시간 관리
        private EditorTimeProvider timeProvider;

        // 프리뷰용 ChartData
        private ChartData previewChartData;
        private Queue<LaneData> remainingLanes;
        private double barDuration;
        private int currentPreviewBar;
        private int endPreviewBar;

        // 오브젝트 풀
        private Queue<GameObject> timelinePool = new Queue<GameObject>();
        private Queue<GameObject> notePool = new Queue<GameObject>();
        private List<GameObject> activeTimelineObjects = new List<GameObject>();
        private List<GameObject> activeNoteObjects = new List<GameObject>();

        // 마디 진행 관리
        private bool hasSpawnedBar = false;
        private double nextBarTime;

        private void Start()
        {
            if (editorManager == null)
                editorManager = GetComponent<ChartEditorManager>();
            if (editorManager == null)
                editorManager = GetComponentInParent<ChartEditorManager>();

            timeProvider = new EditorTimeProvider();

            // 일시정지 토글 이벤트 구독
            if (editorManager != null)
                editorManager.OnPauseToggle += TogglePause;
        }

        private void OnDestroy()
        {
            if (editorManager != null)
                editorManager.OnPauseToggle -= TogglePause;
        }

        private void Update()
        {
            if (!timeProvider.IsPlaying || timeProvider.IsPaused) return;

            double currentTime = timeProvider.GetCurrentTime();

            // 마디 시작 시간에 도달하면 스폰 (while: 프레임 스킵 대비)
            while (currentPreviewBar <= endPreviewBar && currentTime >= nextBarTime)
            {
                // 이전 마디의 노트 정리 (타임라인은 자체 소멸)
                ClearActiveNotes();

                SpawnBar(currentPreviewBar);
                currentPreviewBar++;
                nextBarTime += barDuration;
            }

            // 재생 종료 체크: 마지막 마디 타임라인 이동 완료
            if (currentPreviewBar > endPreviewBar)
            {
                if (currentTime > nextBarTime + 1.0) // 마지막 마디 끝 + 1초 여유
                {
                    StopPreview();
                }
            }
        }

        #region 재생 제어

        /// <summary>
        /// 단일 마디 재생
        /// </summary>
        public void PlaySingle(int barNumber)
        {
            StartPreview(barNumber, barNumber);
        }

        /// <summary>
        /// 부분 재생 (현재 마디 + 앞뒤 1마디 = 총 3마디)
        /// </summary>
        public void PlayPartial(int barNumber)
        {
            int startBar = Mathf.Max(0, barNumber - 1);
            int endBar = barNumber + 1;
            StartPreview(startBar, endBar);
        }

        /// <summary>
        /// 전체 재생 (0번 마디부터)
        /// </summary>
        public void PlayFull()
        {
            int lastBar = editorManager.ChartData.GetLastBarNumber();
            StartPreview(0, lastBar);
        }

        private void StartPreview(int startBar, int endBar)
        {
            StopPreview(); // 기존 프리뷰 정리

            barDuration = editorManager.ChartData.GetBarDuration();
            currentPreviewBar = startBar;
            endPreviewBar = endBar;

            // EditorChartData → ChartData 변환
            previewChartData = EditorChartConverter.ToPlayableChartData(
                editorManager.ChartData, startBar, endBar
            );

            remainingLanes = new Queue<LaneData>(previewChartData.GetFullChartList());

            // 시간 오프셋: startBar의 시작 시간
            double startTimeOffset = startBar * barDuration;
            nextBarTime = startTimeOffset;

            timeProvider.Start(startTimeOffset);

            // 음원 재생
            if (editorManager.audioSource != null && editorManager.ChartData.audioClip != null)
            {
                editorManager.audioSource.clip = editorManager.ChartData.audioClip;
                editorManager.audioSource.time = (float)startTimeOffset;
                editorManager.audioSource.Play();
                Debug.Log($"[EditorPreview] Audio playing: {editorManager.ChartData.audioClip.name}, time={startTimeOffset:F2}s");
            }
            else
            {
                Debug.LogWarning($"[EditorPreview] Audio not playing - audioSource={editorManager.audioSource != null}, audioClip={editorManager.ChartData.audioClip != null}");
            }

            // 에디터 상태 갱신
            editorManager.State.isPlaying = true;
            editorManager.State.isPaused = false;
        }

        public void StopPreview()
        {
            timeProvider.Stop();

            // 음원 정지
            if (editorManager != null && editorManager.audioSource != null)
                editorManager.audioSource.Stop();

            // 활성 오브젝트 풀 반환
            ClearActiveObjects();

            // 에디터 상태 갱신
            if (editorManager != null)
            {
                editorManager.State.isPlaying = false;
                editorManager.State.isPaused = false;
            }
        }

        private void TogglePause()
        {
            if (!timeProvider.IsPlaying) return;

            if (timeProvider.IsPaused)
            {
                timeProvider.Resume();
                if (editorManager.audioSource != null)
                    editorManager.audioSource.UnPause();
                editorManager.State.isPaused = false;
            }
            else
            {
                timeProvider.Pause();
                if (editorManager.audioSource != null)
                    editorManager.audioSource.Pause();
                editorManager.State.isPaused = true;
            }
        }

        #endregion

        #region 마디 스폰

        private void SpawnBar(int barNumber)
        {
            // 해당 마디의 LaneData를 dequeue
            List<LaneData> barLanes = new List<LaneData>();
            while (remainingLanes.Count > 0 && remainingLanes.Peek().bar == barNumber)
            {
                barLanes.Add(remainingLanes.Dequeue());
            }

            if (barLanes.Count == 0) return;

            float leftX = editorManager.leftEndpoint.anchoredPosition.x;
            float rightX = editorManager.rightEndpoint.anchoredPosition.x;
            float laneWidth = rightX - leftX;

            // 타임라인 스폰 (그룹별 1개)
            HashSet<int> spawnedGroups = new HashSet<int>();

            foreach (var lane in barLanes)
            {
                int groupID = (lane.line <= 2) ? 0 : 1;

                // 타임라인 (그룹당 1개)
                if (!spawnedGroups.Contains(groupID))
                {
                    spawnedGroups.Add(groupID);
                    SpawnTimeline(lane, groupID);
                }

                // 노트 스폰
                SpawnLaneNotes(lane, laneWidth);
            }
        }

        private void SpawnTimeline(LaneData lane, int groupID)
        {
            GameObject timelineObj = GetFromPool(timelinePool, editorManager.timelinePrefab);
            if (timelineObj == null) return;

            timelineObj.transform.SetParent(editorManager.noteParent, false);

            // 타임라인 Y 위치 설정 (해당 그룹의 레인 위치 기반)
            RectTransform timelineRT = timelineObj.GetComponent<RectTransform>();
            int timelineIndex = groupID; // 0=상단, 1=하단
            // ChartManager의 timelineTransforms와 동일하게 위치 설정
            if (editorManager.laneTransforms.Length > timelineIndex * 2)
            {
                float y = (editorManager.laneTransforms[timelineIndex * 2].anchoredPosition.y
                         + editorManager.laneTransforms[timelineIndex * 2 + 1].anchoredPosition.y) / 2f;
                timelineRT.anchoredPosition = new Vector2(timelineRT.anchoredPosition.x, y);
            }

            TimelineController controller = timelineObj.GetComponent<TimelineController>();
            if (controller != null)
            {
                float startX, endX;
                if (lane.isLTR)
                {
                    startX = editorManager.leftEndpoint.anchoredPosition.x;
                    endX = editorManager.rightEndpoint.anchoredPosition.x;
                }
                else
                {
                    startX = editorManager.rightEndpoint.anchoredPosition.x;
                    endX = editorManager.leftEndpoint.anchoredPosition.x;
                }

                controller.Init(
                    lane.time,
                    barDuration,
                    startX,
                    endX,
                    (tc) => ReturnToPool(timelinePool, tc.gameObject),
                    timeProvider.GetCurrentTime   // 에디터 시간 소스 주입
                );
            }

            activeTimelineObjects.Add(timelineObj);
        }

        private void SpawnLaneNotes(LaneData lane, float laneWidth)
        {
            float noteInterval = laneWidth / lane.beat;
            float laneStartX = lane.isLTR
                ? editorManager.leftEndpoint.anchoredPosition.x
                : editorManager.rightEndpoint.anchoredPosition.x;

            RectTransform laneRT = editorManager.laneTransforms[lane.line - 1];

            foreach (var noteData in lane.Notes)
            {
                GameObject noteObj = GetFromPool(notePool, editorManager.notePrefab);
                if (noteObj == null) continue;

                noteObj.transform.SetParent(editorManager.noteParent, false);

                NoteAdapter noteAdapter = noteObj.GetComponent<NoteAdapter>();
                if (noteAdapter != null)
                {
                    NoteController noteController = noteAdapter.ActivateAndGet(noteData.noteType);

                    Vector2 spawnPos = new Vector2(
                        laneStartX + noteInterval * noteData.index * (lane.isLTR ? 1 : -1),
                        laneRT.anchoredPosition.y
                    );

                    noteController.Init(
                        noteData,
                        spawnPos,
                        lane.isLTR,
                        noteInterval,
                        (returnedNote) => ReturnToPool(notePool, returnedNote.gameObject)
                    );

                    noteController.SetState(NoteState.Active);
                }

                activeNoteObjects.Add(noteObj);
            }
        }

        #endregion

        #region 오브젝트 풀

        private GameObject GetFromPool(Queue<GameObject> pool, GameObject prefab)
        {
            if (pool.Count > 0)
            {
                var obj = pool.Dequeue();
                obj.SetActive(true);
                return obj;
            }

            if (prefab == null)
            {
                Debug.LogWarning("[EditorPreview] Prefab is null");
                return null;
            }

            return Instantiate(prefab);
        }

        private void ReturnToPool(Queue<GameObject> pool, GameObject obj)
        {
            obj.SetActive(false);
            obj.transform.SetParent(editorManager.objectPoolParent, false);
            pool.Enqueue(obj);
        }

        /// <summary>
        /// 활성 노트만 정리 (마디 전환 시 사용, 타임라인은 자체 소멸)
        /// </summary>
        private void ClearActiveNotes()
        {
            foreach (var obj in activeNoteObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    if (editorManager.objectPoolParent != null)
                        obj.transform.SetParent(editorManager.objectPoolParent, false);
                    notePool.Enqueue(obj);
                }
            }
            activeNoteObjects.Clear();
        }

        private void ClearActiveObjects()
        {
            foreach (var obj in activeTimelineObjects)
            {
                if (obj != null)
                {
                    // TimelineController 정지
                    var tc = obj.GetComponent<TimelineController>();
                    if (tc != null) tc.Deactivate();
                    else
                    {
                        obj.SetActive(false);
                        timelinePool.Enqueue(obj);
                    }
                }
            }
            activeTimelineObjects.Clear();

            foreach (var obj in activeNoteObjects)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    if (editorManager.objectPoolParent != null)
                        obj.transform.SetParent(editorManager.objectPoolParent, false);
                    notePool.Enqueue(obj);
                }
            }
            activeNoteObjects.Clear();
        }

        #endregion
    }
}
