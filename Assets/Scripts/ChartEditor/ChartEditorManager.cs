using System.Collections.Generic;
using SCOdyssey.ChartEditor.Analysis;
using SCOdyssey.ChartEditor.Data;
using SCOdyssey.ChartEditor.IO;
using SCOdyssey.ChartEditor.Preview;
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
        public RectTransform[] laneTransforms = new RectTransform[LANE_COUNT];

        [Header("씬 참조 - 컨테이너")]
        public RectTransform noteParent;        // 에디터 노트 표시용 부모
        public RectTransform beatLineParent;    // 비트선 부모
        public Transform objectPoolParent;      // 오브젝트 풀

        [Header("씬 참조 - 프리뷰")]
        public EditorFMODAudio fmodAudio;
        public GameObject timelinePrefab;
        public GameObject notePrefab;
        public EditorPreviewManager previewManager;  // Tab 단축키로 현재마디 재생

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

        // 복사 버퍼 (C키로 저장, V키로 붙여넣기)
        private CopyBuffer _copyBuffer;

        private class CopyBuffer
        {
            public int beat;
            public char[][] laneSequences;  // 딥 카피
            public bool? upperGroupLTR;
            public bool? lowerGroupLTR;
        }

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
                for (int laneIdx = 0; laneIdx < LANE_COUNT; laneIdx++)
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
        /// <param name="beatIndex">그리드 선 번호 (0=leftEndpoint ~ beat=rightEndpoint)</param>
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

            // 그리드 선 번호 → 배열 인덱스 변환
            // [노트 배치 설계]
            // EditorGridInput에서 beatIndex는 "그리드 선 번호" (0=leftEndpoint, beat=rightEndpoint)
            // LTR: 선 0~beat-1이 노트 위치 → arrayIndex = beatIndex (직접 대응)
            //   선 beat(rightEndpoint)는 유효하지 않음
            // RTL: 선 1~beat가 노트 위치 → arrayIndex = beatIndex - 1 (한 칸 왼쪽 선이 배열 첫 번째)
            //   선 0(leftEndpoint)는 유효하지 않음
            // 렌더링은 EditorGridRenderer에서 RTL에 +1 오프셋으로 arrayIndex → 화면 선 번호 복원
            bool isLTR = bar.GetDirection(laneNumber);
            int arrayIndex;
            if (isLTR)
            {
                if (beatIndex >= bar.beat) return; // rightEndpoint는 LTR 노트 위치 아님
                arrayIndex = beatIndex;
            }
            else
            {
                if (beatIndex <= 0) return; // leftEndpoint는 RTL 노트 위치 아님
                arrayIndex = beatIndex - 1;
                if (arrayIndex >= bar.beat) return; // 안전망
            }

            int laneIndex = laneNumber - 1; // 0-based

            // 토글: 노트가 있으면 삭제, 없으면 삽입
            if (bar.laneSequences[laneIndex][arrayIndex] != '0')
            {
                bar.laneSequences[laneIndex][arrayIndex] = '0';
            }
            else
            {
                char noteChar = ((int)State.selectedNoteType).ToString()[0];
                bar.laneSequences[laneIndex][arrayIndex] = noteChar;
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

        #region 자동 채보 생성

        /// <summary>
        /// 난이도별 자동 채보 생성 프리셋
        /// </summary>
        private static (float sensitivity, int beatResolution) GetGeneratePreset(Difficulty difficulty)
        {
            return difficulty switch
            {
                Difficulty.Easy    => (2.5f, 4),
                Difficulty.Normal  => (1.5f, 8),
                Difficulty.Hard    => (1.0f, 8),
                Difficulty.Extreme => (0.7f, 16),
                _ => (1.5f, 8)
            };
        }

        /// <summary>
        /// 음원 분석 → 난이도별 자동 채보 생성
        /// </summary>
        public void GenerateChart(Difficulty difficulty)
        {
            if (fmodAudio == null || !fmodAudio.IsLoaded)
            {
                ShowWarning("음원 파일을 먼저 불러와주세요.");
                return;
            }

            var (sensitivity, beatResolution) = GetGeneratePreset(difficulty);

            // 기존 채보 초기화 (헤더 메타데이터, audioFilePath, filePath 유지)
            string title = ChartData.title;
            string artist = ChartData.artist;
            int level = ChartData.level;
            int bpm = ChartData.bpm;
            string audioFilePath = ChartData.audioFilePath;
            string path = ChartData.filePath;

            ChartData.Clear();
            ChartData.title = title;
            ChartData.artist = artist;
            ChartData.difficulty = difficulty;
            ChartData.level = level;
            ChartData.bpm = bpm;
            ChartData.audioFilePath = audioFilePath;
            ChartData.filePath = path;

            // 0번 마디(준비 마디) 시작지점: 하단 RTL
            ChartData.GetOrCreateBar(0).lowerGroupLTR = false;

            // FMOD를 통해 PCM 샘플 추출 후 onset 감지
            float[] monoSamples = fmodAudio.GetMonoSamples(out int sampleRate);
            if (monoSamples == null)
            {
                ShowWarning("음원 데이터를 읽을 수 없습니다.");
                return;
            }

            List<AudioOnsetDetector.OnsetInfo> onsets =
                AudioOnsetDetector.DetectOnsets(monoSamples, sampleRate, sensitivity);

            // 채보 생성 (Easy/Normal은 상단↔하단 교대 배치)
            bool alternating = (difficulty == Difficulty.Easy || difficulty == Difficulty.Normal);
            AutoChartGenerator.Generate(ChartData, onsets, beatResolution, alternating);

            // 1번 마디로 이동하여 결과 확인
            LoadBar(1);
            Debug.Log($"[ChartEditor] Auto-generated chart ({difficulty}): {onsets.Count} onsets, sensitivity={sensitivity}, beat={beatResolution}");
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

            // P키 / 스페이스바: 프리뷰 일시정지/재개
            if (keyboard.pKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                OnPauseToggle?.Invoke();
            }

            // 입력 필드 포커스 중에는 이하 단축키 비활성화
            if (IsInputFieldFocused()) return;

            // S: 빠른저장
            if (keyboard.sKey.wasPressedThisFrame)
            {
                QuickSave();
            }

            // C: 현재 마디 복사, V: 붙여넣기
            if (keyboard.cKey.wasPressedThisFrame) CopyBar();
            if (keyboard.vKey.wasPressedThisFrame) PasteBar();

            // 1~5: 노트 타입 선택
            if (keyboard.digit1Key.wasPressedThisFrame) SelectNoteType(NoteType.Normal);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectNoteType(NoteType.HoldStart);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectNoteType(NoteType.Holding);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectNoteType(NoteType.HoldEnd);
            if (keyboard.digit5Key.wasPressedThisFrame) SelectNoteType(NoteType.HoldRelease);

            // D: 방향선택 모드 토글
            if (keyboard.dKey.wasPressedThisFrame)
            {
                ToggleDirectionMode();
            }

            // Enter: 현재마디 재생
            if (keyboard.enterKey.wasPressedThisFrame)
            {
                if (previewManager != null)
                    previewManager.PlaySingle(State.currentBar);
            }

            // Tab: 부분재생 (현재마디 ±1, 총 3마디)
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (previewManager != null)
                    previewManager.PlayPartial(State.currentBar);
            }

            // 좌우 화살표: 마디 이동
            if (keyboard.leftArrowKey.wasPressedThisFrame) PrevBar();
            if (keyboard.rightArrowKey.wasPressedThisFrame) NextBar();

            // PgUp/PgDn: 비트 변경 (4→8→16→32 사이클)
            if (keyboard.pageUpKey.wasPressedThisFrame) CycleBeatUp();
            if (keyboard.pageDownKey.wasPressedThisFrame) CycleBeatDown();
        }

        private bool IsInputFieldFocused()
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        #endregion

        #region 도구 선택 / 저장 / 비트 조작

        private static readonly int[] BeatCycleValues = { 4, 8, 16, 32 };

        /// <summary>
        /// 노트 타입 선택 및 NoteInsert 모드로 전환
        /// </summary>
        public void SelectNoteType(NoteType type)
        {
            State.selectedNoteType = type;
            State.currentTool = EditorTool.NoteInsert;
            OnToolChanged?.Invoke();
        }

        /// <summary>
        /// 방향선택 모드 토글
        /// </summary>
        public void ToggleDirectionMode()
        {
            if (State.currentTool == EditorTool.DirectionSelect)
                State.currentTool = EditorTool.None;
            else
                State.currentTool = EditorTool.DirectionSelect;
            OnToolChanged?.Invoke();
        }

        /// <summary>
        /// 빠른저장: 기존 경로에 덮어쓰기. 경로가 없으면 경고 표시.
        /// </summary>
        public void QuickSave()
        {
            if (string.IsNullOrEmpty(ChartData.filePath))
            {
                ShowWarning("저장 경로가 없습니다.\n파일 메뉴에서 '다른이름으로 저장'을 사용하세요.");
                return;
            }

            string chartText = EditorChartConverter.ToChartText(ChartData);
            if (ChartFileIO.SaveToFile(ChartData.filePath, chartText))
                Debug.Log($"[ChartEditor] Quick saved: {ChartData.filePath}");
            else
                ShowWarning("저장에 실패했습니다.");
        }

        /// <summary>
        /// 비트를 사이클 배열에서 한 단계 올림 (4→8→16→32)
        /// </summary>
        public void CycleBeatUp()
        {
            int idx = GetBeatCycleIndex();
            if (idx < BeatCycleValues.Length - 1) idx++;
            SetBeat(BeatCycleValues[idx]);
        }

        /// <summary>
        /// 비트를 사이클 배열에서 한 단계 내림 (32→16→8→4)
        /// </summary>
        public void CycleBeatDown()
        {
            int idx = GetBeatCycleIndex();
            if (idx > 0) idx--;
            SetBeat(BeatCycleValues[idx]);
        }

        private int GetBeatCycleIndex()
        {
            for (int i = 0; i < BeatCycleValues.Length; i++)
            {
                if (BeatCycleValues[i] == State.currentBeat)
                    return i;
            }
            return 0; // 현재 비트가 사이클에 없으면 최소값(4) 기준
        }

        #endregion

        #region 복사/붙여넣기

        /// <summary>
        /// 현재 마디의 노트 배치 + 방향 설정을 버퍼에 복사 (C키).
        /// </summary>
        public void CopyBar()
        {
            EditorBarData bar = CurrentBar;

            var buffer = new CopyBuffer
            {
                beat = bar.beat,
                laneSequences = new char[LANE_COUNT][],
                upperGroupLTR = bar.upperGroupLTR,
                lowerGroupLTR = bar.lowerGroupLTR,
            };
            for (int i = 0; i < LANE_COUNT; i++)
            {
                buffer.laneSequences[i] = new char[bar.beat];
                System.Array.Copy(bar.laneSequences[i], buffer.laneSequences[i], bar.beat);
            }
            _copyBuffer = buffer;

            Debug.Log($"[ChartEditor] 마디 {State.currentBar} 복사 (beat={bar.beat})");
        }

        /// <summary>
        /// 버퍼의 노트 배치 + 방향 설정을 현재 마디에 덮어씌움 (V키).
        /// </summary>
        public void PasteBar()
        {
            if (_copyBuffer == null)
            {
                ShowWarning("복사된 데이터가 없습니다.");
                return;
            }
            if (State.currentBar == 0)
            {
                ShowWarning("0번 마디에는 붙여넣기할 수 없습니다.");
                return;
            }

            EditorBarData bar = CurrentBar;
            bar.beat = _copyBuffer.beat;
            bar.upperGroupLTR = _copyBuffer.upperGroupLTR;
            bar.lowerGroupLTR = _copyBuffer.lowerGroupLTR;
            bar.laneSequences = new char[LANE_COUNT][];
            for (int i = 0; i < LANE_COUNT; i++)
            {
                bar.laneSequences[i] = new char[_copyBuffer.beat];
                System.Array.Copy(_copyBuffer.laneSequences[i], bar.laneSequences[i], _copyBuffer.beat);
            }

            State.currentBeat = bar.beat;
            RefreshDirectionDisplay();
            RefreshGrid();
            OnBeatChanged?.Invoke(bar.beat);

            // 붙여넣기 후 버퍼 리셋 (중복 방지)
            _copyBuffer = null;

            Debug.Log($"[ChartEditor] 마디 {State.currentBar}에 붙여넣기 (beat={bar.beat})");
        }

        #endregion

        #region 이벤트

        public System.Action<int> OnBarChanged;
        public System.Action<int> OnBeatChanged;
        public System.Action OnGridRefreshRequested;
        public System.Action OnPauseToggle;
        public System.Action OnToolChanged;

        #endregion
    }
}
