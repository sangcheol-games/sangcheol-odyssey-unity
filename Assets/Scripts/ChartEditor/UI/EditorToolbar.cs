using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.UI
{
    /// <summary>
    /// 에디터 상단 툴바 8개 버튼 관리.
    /// 드롭다운/팝업 토글 및 마디/비트 네비게이션 처리.
    /// </summary>
    public class EditorToolbar : MonoBehaviour
    {
        [Header("참조")]
        public ChartEditorManager editorManager;

        [Header("파일 버튼")]
        public Button btnFile;
        public GameObject fileDropdownPanel;

        [Header("기본정보 버튼")]
        public Button btnInfo;
        public GameObject infoPopupPanel;

        [Header("마디 네비게이션")]
        public Button btnBarPrev;
        public Button btnBarNext;
        public TMP_InputField barNumberInput;

        [Header("비트 선택")]
        public Button btnBeat;
        public TMP_InputField beatInput;
        public GameObject beatDropdownPanel;
        public Button btnBeat4;
        public Button btnBeat8;
        public Button btnBeat16;
        public Button btnBeat32;

        [Header("노트삽입")]
        public Button btnNoteInsert;
        public GameObject noteDropdownPanel;
        public Button btnNoteNormal;
        public Button btnNoteHoldStart;
        public Button btnNoteHolding;
        public Button btnNoteHoldEnd;

        [Header("방향선택")]
        public Button btnDirection;
        private bool isDirectionMode = false;

        [Header("재생")]
        public Button btnPlay;
        public GameObject playDropdownPanel;

        [Header("저장")]
        public Button btnSave;

        [Header("상태 표시")]
        public TextMeshProUGUI barDisplayText;      // 마디 번호 표시
        public TextMeshProUGUI beatDisplayText;     // 비트 표시
        public TextMeshProUGUI toolDisplayText;     // 현재 도구 표시
        public Image btnDirectionImage;             // 방향 버튼 배경 (활성화 시 색 변경)

        private void Start()
        {
            SetupButtonListeners();
            UpdateDisplay();

            // 에디터 이벤트 구독
            if (editorManager != null)
            {
                editorManager.OnBarChanged += OnBarChanged;
                editorManager.OnBeatChanged += OnBeatChanged;
            }
        }

        private void OnDestroy()
        {
            if (editorManager != null)
            {
                editorManager.OnBarChanged -= OnBarChanged;
                editorManager.OnBeatChanged -= OnBeatChanged;
            }
        }

        private void SetupButtonListeners()
        {
            // 파일
            if (btnFile != null)
                btnFile.onClick.AddListener(() => TogglePanel(fileDropdownPanel));

            // 기본정보
            if (btnInfo != null)
                btnInfo.onClick.AddListener(() => TogglePanel(infoPopupPanel));

            // 마디 네비게이션
            if (btnBarPrev != null)
                btnBarPrev.onClick.AddListener(() => editorManager.PrevBar());
            if (btnBarNext != null)
                btnBarNext.onClick.AddListener(() => editorManager.NextBar());
            if (barNumberInput != null)
                barNumberInput.onEndEdit.AddListener(OnBarInputSubmit);

            // 비트 선택
            if (btnBeat != null)
                btnBeat.onClick.AddListener(() => TogglePanel(beatDropdownPanel));
            if (beatInput != null)
                beatInput.onEndEdit.AddListener(OnBeatInputSubmit);
            if (btnBeat4 != null)
                btnBeat4.onClick.AddListener(() => SelectBeat(4));
            if (btnBeat8 != null)
                btnBeat8.onClick.AddListener(() => SelectBeat(8));
            if (btnBeat16 != null)
                btnBeat16.onClick.AddListener(() => SelectBeat(16));
            if (btnBeat32 != null)
                btnBeat32.onClick.AddListener(() => SelectBeat(32));

            // 노트삽입
            if (btnNoteInsert != null)
                btnNoteInsert.onClick.AddListener(() => TogglePanel(noteDropdownPanel));
            if (btnNoteNormal != null)
                btnNoteNormal.onClick.AddListener(() => SelectNoteType(NoteType.Normal));
            if (btnNoteHoldStart != null)
                btnNoteHoldStart.onClick.AddListener(() => SelectNoteType(NoteType.HoldStart));
            if (btnNoteHolding != null)
                btnNoteHolding.onClick.AddListener(() => SelectNoteType(NoteType.Holding));
            if (btnNoteHoldEnd != null)
                btnNoteHoldEnd.onClick.AddListener(() => SelectNoteType(NoteType.HoldEnd));

            // 방향선택
            if (btnDirection != null)
                btnDirection.onClick.AddListener(ToggleDirectionMode);

            // 재생
            if (btnPlay != null)
                btnPlay.onClick.AddListener(() => TogglePanel(playDropdownPanel));

            // 저장
            if (btnSave != null)
                btnSave.onClick.AddListener(OnSaveClicked);
        }

        #region 마디 관리

        private void OnBarInputSubmit(string text)
        {
            if (int.TryParse(text, out int barNumber) && barNumber >= 0)
            {
                editorManager.LoadBar(barNumber);
            }
            else
            {
                // 잘못된 입력이면 현재 값으로 복원
                barNumberInput.text = editorManager.State.currentBar.ToString();
            }
        }

        private void OnBarChanged(int barNumber)
        {
            if (barNumberInput != null)
                barNumberInput.text = barNumber.ToString();
            if (barDisplayText != null)
                barDisplayText.text = barNumber.ToString();

            // 비트도 해당 마디의 값으로 갱신
            UpdateBeatDisplay();
        }

        #endregion

        #region 비트 관리

        private void SelectBeat(int beat)
        {
            editorManager.SetBeat(beat);
            CloseAllPanels();
        }

        private void OnBeatInputSubmit(string text)
        {
            if (int.TryParse(text, out int beat) && beat >= 1 && beat <= 128)
            {
                editorManager.SetBeat(beat);
            }
            else
            {
                beatInput.text = editorManager.State.currentBeat.ToString();
            }
        }

        private void OnBeatChanged(int beat)
        {
            UpdateBeatDisplay();
        }

        private void UpdateBeatDisplay()
        {
            int beat = editorManager.State.currentBeat;
            if (beatInput != null)
                beatInput.text = beat.ToString();
            if (beatDisplayText != null)
                beatDisplayText.text = beat.ToString();
        }

        #endregion

        #region 노트 타입 선택

        private void SelectNoteType(NoteType type)
        {
            editorManager.State.selectedNoteType = type;
            editorManager.State.currentTool = EditorTool.NoteInsert;

            // 방향 모드 해제
            isDirectionMode = false;
            UpdateDirectionButtonVisual();

            CloseAllPanels();
            UpdateToolDisplay();
        }

        #endregion

        #region 방향선택 모드

        private void ToggleDirectionMode()
        {
            isDirectionMode = !isDirectionMode;

            if (isDirectionMode)
            {
                editorManager.State.currentTool = EditorTool.DirectionSelect;
            }
            else
            {
                // 이전에 노트삽입 모드였다면 복원, 아니면 None
                editorManager.State.currentTool = EditorTool.None;
            }

            UpdateDirectionButtonVisual();
            UpdateToolDisplay();
        }

        private void UpdateDirectionButtonVisual()
        {
            if (btnDirectionImage != null)
            {
                btnDirectionImage.color = isDirectionMode
                    ? new Color(0.3f, 0.7f, 1f, 1f) // 활성화: 파란색
                    : Color.white;                     // 비활성화: 흰색
            }
        }

        #endregion

        #region 저장

        private void OnSaveClicked()
        {
            var chartData = editorManager.ChartData;

            if (string.IsNullOrEmpty(chartData.filePath))
            {
                // 저장 경로가 없으면 다른이름으로 저장
                string path = IO.ChartFileIO.ShowSaveDialog();
                if (path == null) return;
                chartData.filePath = path;
            }

            string chartText = Data.EditorChartConverter.ToChartText(chartData);
            IO.ChartFileIO.SaveToFile(chartData.filePath, chartText);
        }

        #endregion

        #region 패널 토글

        private void TogglePanel(GameObject panel)
        {
            if (panel == null) return;

            bool isActive = panel.activeSelf;
            CloseAllPanels();

            if (!isActive)
                panel.SetActive(true);
        }

        private void CloseAllPanels()
        {
            if (fileDropdownPanel != null) fileDropdownPanel.SetActive(false);
            if (infoPopupPanel != null) infoPopupPanel.SetActive(false);
            if (beatDropdownPanel != null) beatDropdownPanel.SetActive(false);
            if (noteDropdownPanel != null) noteDropdownPanel.SetActive(false);
            if (playDropdownPanel != null) playDropdownPanel.SetActive(false);
        }

        #endregion

        #region 표시 갱신

        private void UpdateDisplay()
        {
            if (editorManager == null) return;

            OnBarChanged(editorManager.State.currentBar);
            UpdateBeatDisplay();
            UpdateToolDisplay();
        }

        private void UpdateToolDisplay()
        {
            if (toolDisplayText == null) return;

            var state = editorManager.State;
            toolDisplayText.text = state.currentTool switch
            {
                EditorTool.NoteInsert => $"노트: {GetNoteTypeName(state.selectedNoteType)}",
                EditorTool.DirectionSelect => "방향선택",
                _ => ""
            };
        }

        private string GetNoteTypeName(NoteType type)
        {
            return type switch
            {
                NoteType.Normal => "일반",
                NoteType.HoldStart => "홀드시작",
                NoteType.Holding => "홀딩",
                NoteType.HoldEnd => "홀드끝",
                _ => ""
            };
        }

        #endregion
    }
}
