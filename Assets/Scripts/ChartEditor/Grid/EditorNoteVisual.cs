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

        private GameObject holdBarObject;
        private RectTransform holdBarRT;
        private Image holdBarImage;

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

            // 홀드바 오브젝트 동적 생성 (Canvas 없이 순수 Image만 사용)
            // Unity UI에서 자식은 부모 위에 렌더링되므로, 헤드와 겹치는 부분은 바가 위에 표시됨
            holdBarObject = new GameObject("HoldBar", typeof(RectTransform), typeof(Image));
            holdBarObject.transform.SetParent(transform, false);
            holdBarRT = holdBarObject.GetComponent<RectTransform>();
            holdBarImage = holdBarObject.GetComponent<Image>();
            holdBarRT.anchorMin = holdBarRT.anchorMax = new Vector2(0.5f, 0.5f);
            holdBarRT.pivot = new Vector2(0.5f, 0.5f);

            holdBarObject.SetActive(false);
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
        /// <param name="noteInterval">비트 간격 px</param>
        /// <param name="holdBarBeats">HoldStart 전용: 홀드바가 뻗을 비트 수</param>
        public void Init(NoteType noteType, Vector2 position, int beatIndex, int laneNumber, bool isLTR, int beatCount, float noteInterval, int holdBarBeats = 1)
        {
            NoteType = noteType;
            BeatIndex = beatIndex;
            LaneNumber = laneNumber;

            rectTransform.anchoredPosition = position;

            Color noteColor = GetNoteColor(noteType);

            // 소팅 순서: 판정 순서대로 (LTR은 왼쪽이 앞, RTL은 오른쪽이 앞)
            sortingCanvas.sortingOrder = isLTR ? beatCount - beatIndex : beatIndex;

            switch (noteType)
            {
                case NoteType.HoldStart:
                    // 헤드 + 홀드바 (HoldEnd/HoldRelease까지 맞춤형 길이)
                    image.enabled = true;
                    image.color = noteColor;
                    rectTransform.sizeDelta = new Vector2(30f, 30f);
                    ShowHoldBar(isLTR, holdBarBeats * noteInterval, noteColor);
                    break;

                case NoteType.Holding:
                    // 게임에선 비표시, 에디터에서는 소형 반투명 마커로 위치 표시
                    image.enabled = true;
                    image.color = new Color(noteColor.r, noteColor.g, noteColor.b, 0.6f);
                    rectTransform.sizeDelta = new Vector2(16f, 16f);
                    holdBarObject.SetActive(false);
                    break;

                case NoteType.HoldEnd:
                    // 게임에선 비표시, 에디터에서는 소형 반투명 마커로 위치 표시
                    image.enabled = true;
                    image.color = new Color(noteColor.r, noteColor.g, noteColor.b, 0.6f);
                    rectTransform.sizeDelta = new Vector2(16f, 16f);
                    holdBarObject.SetActive(false);
                    break;

                case NoteType.HoldRelease:
                    // 헤드만 표시 (릴리즈 판정)
                    image.enabled = true;
                    image.color = noteColor;
                    rectTransform.sizeDelta = new Vector2(30f, 30f);
                    holdBarObject.SetActive(false);
                    break;

                default: // Normal
                    image.enabled = true;
                    image.color = noteColor;
                    rectTransform.sizeDelta = new Vector2(30f, 30f);
                    holdBarObject.SetActive(false);
                    break;
            }

            gameObject.SetActive(true);
        }

        private void ShowHoldBar(bool isLTR, float barWidth, Color noteColor)
        {
            holdBarObject.SetActive(true);

            // 진행 방향으로 barWidth만큼 뻗음
            float offsetX = isLTR ? barWidth / 2f : -barWidth / 2f;
            holdBarRT.anchoredPosition = new Vector2(offsetX, 0f);
            holdBarRT.sizeDelta = new Vector2(barWidth, 8f);

            // 바 색상: 노트 색상 기반, 반투명
            Color barColor = noteColor;
            barColor.a = 0.6f;
            holdBarImage.color = barColor;
        }

        /// <summary>
        /// 노트 비활성화 (풀 반환용)
        /// </summary>
        public void Deactivate()
        {
            holdBarObject.SetActive(false);
            gameObject.SetActive(false);
        }

        private Color GetNoteColor(NoteType type)
        {
            return type switch
            {
                NoteType.Normal      => new Color(0.2f, 0.6f, 1f,   1f),   // 파랑
                NoteType.HoldStart   => new Color(0.2f, 0.8f, 0.2f, 1f),   // 초록
                NoteType.Holding     => new Color(0.6f, 0.8f, 0.2f, 1f),   // 연두 (비표시지만 색상 보존)
                NoteType.HoldEnd     => new Color(1f,   0.6f, 0.2f, 1f),   // 주황 (비표시지만 색상 보존)
                NoteType.HoldRelease => new Color(1f,   0.3f, 0.5f, 1f),   // 분홍/핑크
                _                    => Color.white
            };
        }
    }
}
