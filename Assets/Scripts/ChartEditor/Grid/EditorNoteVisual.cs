using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.Grid
{
    /// <summary>
    /// 에디터에서 노트를 시각적으로 표시하는 컴포넌트.
    /// 게임용 NoteController와 달리 단순 이미지 표시만 담당.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class EditorNoteVisual : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Image image;
        private Canvas sortingCanvas;

        public NoteType NoteType { get; private set; }
        public int BeatIndex { get; private set; }
        public int LaneNumber { get; private set; }

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            image = GetComponent<Image>();

            // 소팅용 Canvas 오버라이드
            sortingCanvas = gameObject.GetComponent<Canvas>();
            if (sortingCanvas == null)
                sortingCanvas = gameObject.AddComponent<Canvas>();
            sortingCanvas.overrideSorting = true;
        }

        /// <summary>
        /// 노트 초기화
        /// </summary>
        /// <param name="noteType">노트 타입</param>
        /// <param name="position">앵커 위치</param>
        /// <param name="beatIndex">비트 인덱스 (소팅용)</param>
        /// <param name="laneNumber">레인 번호 (1~4)</param>
        /// <param name="isLTR">진행 방향</param>
        /// <param name="beatCount">총 비트수 (소팅 순서 계산용)</param>
        public void Init(NoteType noteType, Vector2 position, int beatIndex, int laneNumber, bool isLTR, int beatCount)
        {
            NoteType = noteType;
            BeatIndex = beatIndex;
            LaneNumber = laneNumber;

            rectTransform.anchoredPosition = position;

            // NoteType별 색상
            image.color = GetNoteColor(noteType);

            // 노트 크기 (고정)
            rectTransform.sizeDelta = new Vector2(30f, 30f);

            // 소팅 순서: 판정 순서대로 (LTR은 왼쪽이 앞, RTL은 오른쪽이 앞)
            // sortingOrder가 높을수록 앞에 표시
            if (isLTR)
            {
                // LTR: 왼쪽(작은 index)이 앞 → 큰 sortingOrder
                sortingCanvas.sortingOrder = beatCount - beatIndex;
            }
            else
            {
                // RTL: 오른쪽(큰 index)이 앞 → index가 클수록 높은 sortingOrder
                sortingCanvas.sortingOrder = beatIndex;
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// 노트 비활성화 (풀 반환용)
        /// </summary>
        public void Deactivate()
        {
            gameObject.SetActive(false);
        }

        private Color GetNoteColor(NoteType type)
        {
            return type switch
            {
                NoteType.Normal => new Color(0.2f, 0.6f, 1f, 1f),      // 파랑
                NoteType.HoldStart => new Color(0.2f, 0.8f, 0.2f, 1f), // 초록
                NoteType.Holding => new Color(0.6f, 0.8f, 0.2f, 0.7f), // 연두 (반투명)
                NoteType.HoldEnd => new Color(1f, 0.6f, 0.2f, 1f),     // 주황
                _ => Color.white
            };
        }
    }
}
