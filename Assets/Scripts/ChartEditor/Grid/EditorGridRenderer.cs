using System.Collections.Generic;
using SCOdyssey.ChartEditor.Data;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.Grid
{
    /// <summary>
    /// 에디터 비트 그리드 렌더링.
    /// 비트선(세로선) 렌더링 + 노트 시각화 + 방향 표시.
    /// </summary>
    public class EditorGridRenderer : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        [Header("프리팹")]
        public GameObject beatLinePrefab;       // 비트선용 Image 프리팹 (가로 1~2px, 세로 레인 높이)
        public GameObject editorNotePrefab;     // EditorNoteVisual이 붙은 프리팹

        // 오브젝트 풀
        private Queue<GameObject> beatLinePool = new Queue<GameObject>();
        private Queue<GameObject> noteVisualPool = new Queue<GameObject>();

        // 현재 활성 오브젝트 목록 (Refresh 시 풀 반환용)
        private List<GameObject> activeBeatLines = new List<GameObject>();
        private List<EditorNoteVisual> activeNotes = new List<EditorNoteVisual>();

        private void Start()
        {
            if (editorManager == null)
                editorManager = GetComponent<ChartEditorManager>();
            if (editorManager == null)
                editorManager = GetComponentInParent<ChartEditorManager>();

            // 이벤트 구독
            if (editorManager != null)
            {
                editorManager.OnGridRefreshRequested += Refresh;
            }
        }

        private void OnDestroy()
        {
            if (editorManager != null)
            {
                editorManager.OnGridRefreshRequested -= Refresh;
            }
        }

        /// <summary>
        /// 현재 마디의 그리드와 노트를 새로고침
        /// </summary>
        public void Refresh()
        {
            ClearAll();

            if (editorManager == null) return;

            EditorBarData bar = editorManager.CurrentBar;
            int beat = bar.beat;

            DrawBeatLines(beat);
            DrawNotes(bar);
        }

        #region 비트선 렌더링

        private void DrawBeatLines(int beat)
        {
            float leftX = editorManager.leftEndpoint.anchoredPosition.x;
            float rightX = editorManager.rightEndpoint.anchoredPosition.x;
            float laneWidth = rightX - leftX;
            float interval = laneWidth / beat;

            // 비트선: 0~beat (총 beat+1개, endpoint 포함)
            for (int i = 0; i <= beat; i++)
            {
                float lineX = leftX + interval * i;

                // endpoint 위치의 선은 굵게 표시
                bool isEndpoint = (i == 0 || i == beat);

                GameObject lineObj = GetBeatLine();
                RectTransform lineRT = lineObj.GetComponent<RectTransform>();
                Image lineImage = lineObj.GetComponent<Image>();

                // 4개 레인 전체를 관통하는 세로선
                float topY = editorManager.laneTransforms[0].anchoredPosition.y;
                float bottomY = editorManager.laneTransforms[3].anchoredPosition.y;
                float lineHeight = Mathf.Abs(topY - bottomY) + 60f; // 레인 높이 + 여유

                lineRT.anchoredPosition = new Vector2(lineX, (topY + bottomY) / 2f);
                lineRT.sizeDelta = new Vector2(isEndpoint ? 3f : 1f, lineHeight);

                // endpoint 선은 흰색, 비트선은 반투명 회색
                lineImage.color = isEndpoint
                    ? new Color(1f, 1f, 1f, 0.8f)
                    : new Color(0.6f, 0.6f, 0.6f, 0.4f);

                lineObj.SetActive(true);
                activeBeatLines.Add(lineObj);
            }
        }

        #endregion

        #region 노트 렌더링

        private void DrawNotes(EditorBarData bar)
        {
            float leftX = editorManager.leftEndpoint.anchoredPosition.x;
            float rightX = editorManager.rightEndpoint.anchoredPosition.x;
            float laneWidth = rightX - leftX;
            float noteInterval = laneWidth / bar.beat;

            for (int laneIdx = 0; laneIdx < 4; laneIdx++)
            {
                int laneNumber = laneIdx + 1;
                if (!bar.IsDirectionSet(laneNumber)) continue;

                bool isLTR = bar.GetDirection(laneNumber);
                float laneY = editorManager.laneTransforms[laneIdx].anchoredPosition.y;

                for (int beatIdx = 0; beatIdx < bar.beat; beatIdx++)
                {
                    char noteChar = bar.laneSequences[laneIdx][beatIdx];
                    if (noteChar == '0') continue;

                    NoteType noteType = (NoteType)(noteChar - '0');

                    // 화면상 왼→오 순서로 비트선 위치 계산
                    float noteX = leftX + noteInterval * beatIdx;
                    Vector2 notePos = new Vector2(noteX, laneY);

                    EditorNoteVisual noteVisual = GetNoteVisual();
                    noteVisual.Init(noteType, notePos, beatIdx, laneNumber, isLTR, bar.beat);
                    activeNotes.Add(noteVisual);
                }
            }
        }

        #endregion

        #region 오브젝트 풀

        private void ClearAll()
        {
            foreach (var line in activeBeatLines)
            {
                line.SetActive(false);
                beatLinePool.Enqueue(line);
            }
            activeBeatLines.Clear();

            foreach (var note in activeNotes)
            {
                note.Deactivate();
                noteVisualPool.Enqueue(note.gameObject);
            }
            activeNotes.Clear();
        }

        private GameObject GetBeatLine()
        {
            if (beatLinePool.Count > 0)
            {
                return beatLinePool.Dequeue();
            }

            // 비트선 프리팹이 없으면 동적 생성
            GameObject lineObj;
            if (beatLinePrefab != null)
            {
                lineObj = Instantiate(beatLinePrefab, editorManager.beatLineParent);
            }
            else
            {
                lineObj = new GameObject("BeatLine", typeof(RectTransform), typeof(Image));
                lineObj.transform.SetParent(editorManager.beatLineParent, false);
            }
            return lineObj;
        }

        private EditorNoteVisual GetNoteVisual()
        {
            if (noteVisualPool.Count > 0)
            {
                var obj = noteVisualPool.Dequeue();
                return obj.GetComponent<EditorNoteVisual>();
            }

            GameObject noteObj;
            if (editorNotePrefab != null)
            {
                noteObj = Instantiate(editorNotePrefab, editorManager.noteParent);
            }
            else
            {
                noteObj = new GameObject("EditorNote", typeof(RectTransform), typeof(Image), typeof(EditorNoteVisual));
                noteObj.transform.SetParent(editorManager.noteParent, false);
            }
            return noteObj.GetComponent<EditorNoteVisual>();
        }

        #endregion
    }
}
