using SCOdyssey.ChartEditor.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor
{
    /// <summary>
    /// 채보 에디터의 메인 컨트롤러.
    /// EditorChartData 관리, 툴바 이벤트 중재, 마디/도구 상태 관리.
    /// </summary>
    public class ChartEditorManager : MonoBehaviour
    {
        [Header("씬 참조 - 레인 & 스크롤")]
        public RectTransform leftEndpoint;
        public RectTransform rightEndpoint;
        public RectTransform[] laneTransforms = new RectTransform[4];

        [Header("씬 참조 - 컨테이너")]
        public RectTransform noteParent;        // 에디터 노트 표시용 부모
        public RectTransform beatLineParent;    // 비트선 부모
        public Transform objectPoolParent;      // 오브젝트 풀

        [Header("씬 참조 - 프리뷰")]
        public AudioSource audioSource;
        public GameObject timelinePrefab;
        public GameObject notePrefab;

        [Header("씬 참조 - 방향 표시")]
        public RectTransform[] directionZonesLeft = new RectTransform[2];   // 상단/하단 좌측 클릭 영역
        public RectTransform[] directionZonesRight = new RectTransform[2];  // 상단/하단 우측 클릭 영역
        public TextMeshProUGUI[] directionTextsLeft = new TextMeshProUGUI[2];   // "시작지점" 텍스트
        public TextMeshProUGUI[] directionTextsRight = new TextMeshProUGUI[2];

        [Header("경고 팝업")]
        public GameObject warningPopup;
        public TextMeshProUGUI warningText;
        public Button btnWarningClose;         // 경고 팝업 확인 버튼

        // 에디터 데이터
        public EditorChartData ChartData { get; private set; }
        public EditorState State { get; private set; }

        // 현재 작업 중인 마디 데이터
        public EditorBarData CurrentBar => ChartData.GetOrCreateBar(State.currentBar);

        /// <summary>
        /// 채보 데이터 전체 교체 (파일 불러오기 시 사용)
        /// </summary>
        public void ReplaceChartData(EditorChartData newData)
        {
            ChartData = newData;
        }

        private void Awake()
        {
            ChartData = new EditorChartData();
            State = new EditorState();
        }

        private void Start()
        {
            // 경고 팝업 확인 버튼 리스너
            if (btnWarningClose != null)
                btnWarningClose.onClick.AddListener(CloseWarning);

            // 0번 마디로 시작
            LoadBar(0);
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
        }

        #region 마디 관리

        /// <summary>
        /// 특정 마디를 로드하여 화면에 표시
        /// </summary>
        public void LoadBar(int barNumber)
        {
            if (barNumber < 0) return;

            State.currentBar = barNumber;
            EditorBarData bar = ChartData.GetOrCreateBar(barNumber);
            State.currentBeat = bar.beat;

            // 방향 표시 갱신
            RefreshDirectionDisplay();
            // 그리드 + 노트 렌더링 갱신
            RefreshGrid();

            OnBarChanged?.Invoke(barNumber);
        }

        /// <summary>
        /// 다음 마디로 이동
        /// </summary>
        public void NextBar()
        {
            LoadBar(State.currentBar + 1);
        }

        /// <summary>
        /// 이전 마디로 이동
        /// </summary>
        public void PrevBar()
        {
            LoadBar(State.currentBar - 1);
        }

        #endregion

        #region 비트 관리

        /// <summary>
        /// 현재 마디의 비트 분할수 변경
        /// </summary>
        public void SetBeat(int newBeat)
        {
            if (newBeat < 1) return;

            EditorBarData bar = CurrentBar;

            // 비트가 줄어들 때 범위 밖의 노트가 있으면 경고
            if (newBeat < bar.beat)
            {
                bool hasNotesOutOfRange = false;
                for (int laneIdx = 0; laneIdx < 4; laneIdx++)
                {
                    for (int i = newBeat; i < bar.beat; i++)
                    {
                        if (bar.laneSequences[laneIdx][i] != '0')
                        {
                            hasNotesOutOfRange = true;
                            break;
                        }
                    }
                    if (hasNotesOutOfRange) break;
                }

                if (hasNotesOutOfRange)
                {
                    Debug.LogWarning($"[ChartEditor] 비트 변경 시 일부 노트가 삭제됩니다. ({bar.beat} → {newBeat})");
                    // TODO: 확인 팝업 구현 시 여기서 사용자 확인 후 진행
                }
            }

            bar.SetBeat(newBeat);
            State.currentBeat = newBeat;

            RefreshGrid();
            OnBeatChanged?.Invoke(newBeat);
        }

        #endregion

        #region 방향 설정

        /// <summary>
        /// 레인 그룹의 방향 설정
        /// </summary>
        /// <param name="groupIndex">0 = 상단(레인1,2), 1 = 하단(레인3,4)</param>
        /// <param name="isLTR">true = LTR, false = RTL</param>
        public void SetDirection(int groupIndex, bool isLTR)
        {
            EditorBarData bar = CurrentBar;

            if (groupIndex == 0)
            {
                // 같은 방향 재클릭 시 해제
                if (bar.upperGroupLTR.HasValue && bar.upperGroupLTR.Value == isLTR)
                {
                    bar.upperGroupLTR = null;
                }
                else
                {
                    bar.upperGroupLTR = isLTR;
                }
            }
            else
            {
                if (bar.lowerGroupLTR.HasValue && bar.lowerGroupLTR.Value == isLTR)
                {
                    bar.lowerGroupLTR = null;
                }
                else
                {
                    bar.lowerGroupLTR = isLTR;
                }
            }

            RefreshDirectionDisplay();
            RefreshGrid();
        }

        #endregion

        #region 노트 배치

        /// <summary>
        /// 노트 배치/삭제 토글
        /// </summary>
        /// <param name="laneNumber">레인 번호 (1~4)</param>
        /// <param name="beatIndex">비트 위치 인덱스 (화면 왼쪽→오른쪽)</param>
        public void ToggleNote(int laneNumber, int beatIndex)
        {
            EditorBarData bar = CurrentBar;

            // 0번 마디는 노트 배치 불가
            if (State.currentBar == 0)
            {
                ShowWarning("0번 마디에는 노트를 배치할 수 없습니다.");
                return;
            }

            // 방향 미설정 확인
            if (!bar.IsDirectionSet(laneNumber))
            {
                ShowWarning("방향을 먼저 설정해주세요.");
                return;
            }

            // 끝 경계 확인
            bool isLTR = bar.GetDirection(laneNumber);
            if (isLTR && beatIndex >= bar.beat)
            {
                return; // LTR에서 rightEndpoint 위치는 범위 밖
            }
            if (!isLTR && beatIndex <= 0)
            {
                return; // RTL에서 leftEndpoint 위치는 범위 밖
            }

            int laneIndex = laneNumber - 1; // 0-based

            // 토글: 노트가 있으면 삭제, 없으면 삽입
            if (bar.laneSequences[laneIndex][beatIndex] != '0')
            {
                bar.laneSequences[laneIndex][beatIndex] = '0';
            }
            else
            {
                char noteChar = ((int)State.selectedNoteType).ToString()[0];
                bar.laneSequences[laneIndex][beatIndex] = noteChar;
            }

            RefreshGrid();
        }

        #endregion

        #region 화면 갱신

        private void RefreshGrid()
        {
            // Phase 2에서 EditorGridRenderer 연결
            OnGridRefreshRequested?.Invoke();
        }

        private void RefreshDirectionDisplay()
        {
            EditorBarData bar = CurrentBar;

            for (int groupIdx = 0; groupIdx < 2; groupIdx++)
            {
                bool? direction = groupIdx == 0 ? bar.upperGroupLTR : bar.lowerGroupLTR;

                bool showLeft = direction.HasValue && direction.Value;   // LTR: 시작지점이 왼쪽
                bool showRight = direction.HasValue && !direction.Value; // RTL: 시작지점이 오른쪽

                if (directionTextsLeft[groupIdx] != null)
                    directionTextsLeft[groupIdx].gameObject.SetActive(showLeft);

                if (directionTextsRight[groupIdx] != null)
                    directionTextsRight[groupIdx].gameObject.SetActive(showRight);
            }
        }

        #endregion

        #region 경고 팝업

        public void ShowWarning(string message)
        {
            if (warningPopup != null)
            {
                warningText.text = message;
                warningPopup.SetActive(true);
            }
            else
            {
                Debug.LogWarning($"[ChartEditor] {message}");
            }
        }

        public void CloseWarning()
        {
            if (warningPopup != null)
                warningPopup.SetActive(false);
        }

        #endregion

        #region 키보드 단축키

        private void HandleKeyboardShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // P키: 프리뷰 일시정지/재개
            if (keyboard.pKey.wasPressedThisFrame)
            {
                OnPauseToggle?.Invoke();
            }

            // 좌우 화살표: 마디 이동 (툴바 포커스가 아닐 때)
            if (keyboard.leftArrowKey.wasPressedThisFrame && !IsInputFieldFocused())
            {
                PrevBar();
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame && !IsInputFieldFocused())
            {
                NextBar();
            }
        }

        private bool IsInputFieldFocused()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        #endregion

        #region 이벤트

        public System.Action<int> OnBarChanged;
        public System.Action<int> OnBeatChanged;
        public System.Action OnGridRefreshRequested;
        public System.Action OnPauseToggle;

        #endregion
    }
}
