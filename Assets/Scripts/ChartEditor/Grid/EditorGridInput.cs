using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SCOdyssey.ChartEditor.Grid
{
    /// <summary>
    /// 에디터 그리드 클릭 입력 처리.
    /// 마우스 클릭 → (lane, beatIndex) 매핑 + 방향 클릭 영역 처리.
    /// 전체 그리드를 덮는 투명 패널에 부착.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class EditorGridInput : MonoBehaviour, IPointerClickHandler
    {
        [Header("참조")]
        public ChartEditorManager editorManager;
        private RectTransform canvasRT;

        [Header("설정")]
        [SerializeField] private Camera uiCamera;   // Canvas가 Screen Space - Camera인 경우

        private float laneDetectionRadius;  // 레인 판별 반경 (동적 계산)

        private void Start()
        {
            if (editorManager == null)
                editorManager = GetComponentInParent<ChartEditorManager>();

            // Canvas의 RectTransform 캐싱
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                canvasRT = canvas.GetComponent<RectTransform>();

            // 투명 Image 설정 (Raycast 수신용)
            Image image = GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0, 0, 0, 0); // 완전 투명
                image.raycastTarget = true;
            }

            // 레인 간격 기반 판별 반경 계산
            if (editorManager != null && editorManager.laneTransforms.Length >= 2)
            {
                float lane1Y = editorManager.laneTransforms[0].anchoredPosition.y;
                float lane2Y = editorManager.laneTransforms[1].anchoredPosition.y;
                laneDetectionRadius = Mathf.Abs(lane1Y - lane2Y) / 2f; // 레인 간격의 절반
            }
            else
            {
                laneDetectionRadius = 120f; // 기본값
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"[GridInput] OnPointerClick! editorManager={editorManager != null}, canvasRT={canvasRT != null}");

            if (editorManager == null)
            {
                Debug.LogWarning("[GridInput] editorManager is null!");
                return;
            }

            // 프리뷰 재생 중에는 입력 차단
            if (editorManager.State.isPlaying && !editorManager.State.isPaused) return;

            // 클릭 위치를 Canvas 로컬 좌표로 변환
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRT, eventData.position, uiCamera, out localPoint
            );

            Debug.Log($"[GridInput] localPoint={localPoint}, tool={editorManager.State.currentTool}");

            // 방향선택 모드인지 확인
            if (editorManager.State.currentTool == EditorTool.DirectionSelect)
            {
                TryHandleDirectionClick(localPoint);
            }
            else if (editorManager.State.currentTool == EditorTool.NoteInsert)
            {
                TryHandleNoteClick(localPoint);
            }
            else
            {
                Debug.Log("[GridInput] currentTool is None - 방향선택 또는 노트삽입 버튼을 먼저 클릭하세요");
            }
        }

        #region 방향 클릭 처리

        private void TryHandleDirectionClick(Vector2 localPoint)
        {
            float leftX = editorManager.leftEndpoint.anchoredPosition.x;
            float rightX = editorManager.rightEndpoint.anchoredPosition.x;

            // 클릭이 leftEndpoint 왼쪽인지, rightEndpoint 오른쪽인지 확인
            bool isLeftSide = localPoint.x < leftX;
            bool isRightSide = localPoint.x > rightX;

            Debug.Log($"[GridInput] Direction click: localX={localPoint.x:F1}, leftX={leftX:F1}, rightX={rightX:F1}, isLeft={isLeftSide}, isRight={isRightSide}");

            if (!isLeftSide && !isRightSide)
            {
                Debug.Log("[GridInput] endpoint 사이 클릭 - 방향 설정은 endpoint 바깥쪽을 클릭하세요");
                return;
            }

            // 어떤 그룹(상단/하단)인지 Y좌표로 판별
            int groupIndex = GetGroupIndexFromY(localPoint.y);
            Debug.Log($"[GridInput] groupIndex={groupIndex} (localY={localPoint.y:F1})");
            if (groupIndex < 0) return;

            if (isLeftSide)
            {
                // 왼쪽 클릭 → LTR (시작지점이 왼쪽)
                Debug.Log($"[GridInput] SetDirection group={groupIndex} LTR");
                editorManager.SetDirection(groupIndex, true);
            }
            else
            {
                // 오른쪽 클릭 → RTL (시작지점이 오른쪽)
                Debug.Log($"[GridInput] SetDirection group={groupIndex} RTL");
                editorManager.SetDirection(groupIndex, false);
            }
        }

        #endregion

        #region 노트 클릭 처리

        private void TryHandleNoteClick(Vector2 localPoint)
        {
            float leftX = editorManager.leftEndpoint.anchoredPosition.x;
            float rightX = editorManager.rightEndpoint.anchoredPosition.x;
            float laneWidth = rightX - leftX;

            Debug.Log($"[GridInput] Note click: localPoint={localPoint}, leftX={leftX:F1}, rightX={rightX:F1}");

            // endpoint 범위 밖이면 무시
            if (localPoint.x < leftX - 10f || localPoint.x > rightX + 10f)
            {
                Debug.Log("[GridInput] 클릭이 endpoint 범위 밖");
                return;
            }

            int beat = editorManager.State.currentBeat;
            float interval = laneWidth / beat;

            // 가장 가까운 비트 인덱스로 스냅
            float relativeX = localPoint.x - leftX;
            int beatIndex = Mathf.RoundToInt(relativeX / interval);
            beatIndex = Mathf.Clamp(beatIndex, 0, beat);

            // 레인 판별
            int laneNumber = GetLaneNumberFromY(localPoint.y);
            Debug.Log($"[GridInput] beatIndex={beatIndex}, laneNumber={laneNumber} (laneDetectionRadius={laneDetectionRadius:F1})");
            if (laneNumber < 1 || laneNumber > 4)
            {
                Debug.Log("[GridInput] 레인 판별 실패 - 레인 근처를 클릭하세요");
                return;
            }

            // ChartEditorManager에 노트 토글 요청
            editorManager.ToggleNote(laneNumber, beatIndex);
        }

        #endregion

        #region 좌표 → 레인/그룹 매핑

        /// <summary>
        /// Y좌표로 가장 가까운 레인 번호 반환 (1~4, 범위 밖이면 -1)
        /// </summary>
        private int GetLaneNumberFromY(float y)
        {
            float closestDist = float.MaxValue;
            int closestLane = -1;

            for (int i = 0; i < 4; i++)
            {
                float laneY = editorManager.laneTransforms[i].anchoredPosition.y;
                float dist = Mathf.Abs(y - laneY);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestLane = i + 1; // 1-based
                }
            }

            // 레인 판별 반경 밖이면 무효
            if (closestDist > laneDetectionRadius) return -1;

            return closestLane;
        }

        /// <summary>
        /// Y좌표로 그룹 인덱스 반환 (0=상단, 1=하단, -1=범위 밖)
        /// </summary>
        private int GetGroupIndexFromY(float y)
        {
            // 상단 그룹 (레인 1, 2)의 Y 범위
            float lane1Y = editorManager.laneTransforms[0].anchoredPosition.y;
            float lane2Y = editorManager.laneTransforms[1].anchoredPosition.y;
            float upperCenter = (lane1Y + lane2Y) / 2f;

            // 하단 그룹 (레인 3, 4)의 Y 범위
            float lane3Y = editorManager.laneTransforms[2].anchoredPosition.y;
            float lane4Y = editorManager.laneTransforms[3].anchoredPosition.y;
            float lowerCenter = (lane3Y + lane4Y) / 2f;

            float distToUpper = Mathf.Abs(y - upperCenter);
            float distToLower = Mathf.Abs(y - lowerCenter);

            // 두 그룹 사이의 중간보다 위면 상단, 아래면 하단
            float groupBoundary = (upperCenter + lowerCenter) / 2f;

            if (y > groupBoundary - 100f && y < lane1Y + 100f)
                return 0; // 상단
            if (y < groupBoundary + 100f && y > lane4Y - 100f)
                return 1; // 하단

            return -1;
        }

        #endregion
    }
}
