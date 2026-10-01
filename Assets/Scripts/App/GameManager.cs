using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Game;
using SCOdyssey.Game.Timing;
using SCOdyssey.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // ── 흐름 (게임 오케스트레이터) ──────────────────────────────────────────
    //
    //  준비: Awake()에서 자신을 ServiceLocator에 등록하고 ISongPlayer와 JudgementDriver를 얻는다.
    //        Start()에서 IInputManager의 재시작·일시정지 이벤트와 ScoreManager의 UI 이벤트를 구독한다.
    //
    //  게임 시작: GameDataLoader가 곡 세션을 연 뒤 StartGame()을 호출한다.
    //        판정 타이밍 연결(GameplayTimingBinding) -> chartManager.Init()(안에서 StartMusic -> 세션 Start) -> scoreManager.Init()
    //
    //  시간: GetCurrentTime()은 곡 시계의 곡 시각(Frame.SongTime)을 돌려준다. 0은 게임 시작, 음원은 리드인 + 노트 싱크 뒤에 시작한다.
    //        일시정지 중에는 세션이 곡 시계를 멈추고, 재개 때 같은 곡 시각에서 다시 흐른다.
    //
    //  매 프레임: JudgementDriver(-900)가 binding을 거쳐 OnTimingAdvance -> chartManager.SyncTime(songTime, judgeTime)
    //
    //  입력: JudgementDriver가 입력 시각을 곡 시각·판정 시각으로 바꿔 OnTimingLaneInput으로 넘긴다
    //        -> chartManager.TryJudgeInput()/TryJudgeRelease() (판정 싱크는 JudgementDriver가 judgeTime에 한 번만 적용한다)
    //
    //  판정 수신: ChartManager가 OnNoteJudged()/OnNoteMissed()/OnHoldStart() 등을 호출하면
    //        scoreManager.ProcessJudge()로 점수를 넘기고, *Event를 발행해 CharacterAnimator에 전파한다.
    //
    //  종료: 채보와 음원이 끝나면 ChartManager가 OnGameFinished()를 호출한다 -> 클리어 연출 -> ResultUI 표시.
    // ──────────────────────────────────────────────────────────────────────────
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        // [AUDIO-IP:G1] 곡 세션과 판정 타이밍
        private ISongPlayer _songs;
        private ISongSession _session;
        private JudgementDriver _judgement;
        private GameplayTimingBinding _timing;
        private IInputManager _inputManager;
        public ScoreManager scoreManager;
        public ChartManager chartManager;
        public ChartData chartData;

        [Header("BGA")]
        public BGAController bgaController; // Inspector 연결 (없으면 BGA 비활성)

        // 캐릭터 애니메이터 구독용 이벤트. 아래 On* 콜백(ChartManager가 호출)이 이 이벤트를 발행하고,
        // CharacterAnimator가 groupID로 필터링해 자기 그룹 이벤트만 처리한다.
        //
        // 판정 이벤트는 일부러 두지 않았다. CheckHoldingBody가 홀드 본체의 Holding 노트마다,
        // TryJudgeRelease가 릴리즈 판정마다 OnNoteJudged를 쏘는데 둘 다 캐릭터에는 잡음이다.
        // 히트 등급은 OnLaneInputEvent가 예측값으로 싣고 오므로 타격음과 그림이 어긋날 수 없다.
        public event Action<NotePosition, int> OnHoldStartEvent;
        public event Action<NotePosition, int> OnHoldStopEvent;
        public event Action<LaneInputResult> OnLaneInputEvent;


        [Header("게임 상태")]
        public bool IsGameRunning { get; private set; } = false;
        public bool IsPaused { get; private set; } = false;
        // [AUDIO-IP:G10] 음원이 끝나기 전까지 true. 음원이 끝난 뒤에도 세션은 일시정지할 수 있다.
        public bool IsAudioPlaying => _session != null && !_session.IsAudioFinished;

        [Header("UI")]
        public Canvas gameCanvas; // GameScene의 메인 Canvas (결과화면 표시 시 비활성화)
        public TextMeshProUGUI scoreText;
        public GameObject comboRoot;   // Combo 그룹 루트("Combo" 라벨 + 숫자 + 인디케이터). 콤보 0이면 통째로 숨긴다
        public TextMeshProUGUI comboText;
        public ComboIndicatorAnimator comboIndicator; // Combo 화살표 밀림 연출. 미할당이면 연출만 생략된다
        public ComboCountAnimator comboCountAnimator; // 콤보 숫자 팝 연출. 미할당이면 연출만 생략된다
        public TextMeshProUGUI gaugeText;
        public Image gaugeBar; // fillAmount로 게이지 바 표현 시
        public Image clearEffectImage; // 클리어 등급 / 재개 카운트다운 연출 이미지

        // ClearType enum 순서(Fail, Clear, FullCombo, OverMillion, AllPerfect)와 인덱스가 일치해야 함
        [SerializeField] private Sprite[] clearTypeSprites = new Sprite[5];

        // 일시정지 재개 카운트다운. GameUI_Countdown_1~3, 인덱스 = 숫자 - 1
        [SerializeField] private Sprite[] resumeCountdownSprites = new Sprite[3];


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
            if (!ServiceLocator.TryGet<ISongPlayer>(out _songs))
                Debug.LogError("[GameManager] ISongPlayer not found in ServiceLocator!");
            if (!ServiceLocator.TryGet<JudgementDriver>(out _judgement))
                Debug.LogError("[GameManager] JudgementDriver not found in ServiceLocator!");

            // gameCanvas가 Screen Space - Camera이면 worldCamera 설정
            if (gameCanvas != null && Camera.main != null
                && gameCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                gameCanvas.worldCamera = Camera.main;
            }
        }

        private void Start()
        {
            // InputManager는 Managers에서 이미 생성 및 등록됨
            if (ServiceLocator.TryGet<IInputManager>(out _inputManager))
            {
                _inputManager.SwitchToGameplay(); // 게임용 키 세팅으로 전환
                // [AUDIO-IP:G2] 레인 입력은 JudgementDriver → binding으로 받는다(OnTimingLaneInput)
                _inputManager.OnRestart += HandleRestart;
                _inputManager.OnPause += HandlePause;
            }
            else
            {
                Debug.LogError("[GameManager] IInputManager not found in ServiceLocator!");
            }

            scoreManager.OnScoreChanged += UpdateScore;
            scoreManager.OnComboChanged += UpdateCombo;
            scoreManager.OnGaugeChanged += UpdateGauge;

            // StartGame(→ scoreManager.Init)은 로딩 화면이 걷히고 시작 전 대기가 끝난 뒤에야 불린다.
            // 그 사이 게임 화면이 먼저 보이므로, 씬에 켜진 채 저장된 Combo 그룹을 여기서 미리 숨긴다.
            UpdateCombo(0);
        }

        private void OnDestroy()
        {
            ServiceLocator.Remove<IGameManager>();

            // [AUDIO-IP:G2] SwitchToUI가 만드는 합성 release보다 먼저 판정 연결을 끊는다
            _timing?.Dispose();
            _timing = null;

            if (_inputManager != null)
            {
                _inputManager.OnRestart -= HandleRestart;
                _inputManager.OnPause -= HandlePause;
                _inputManager.SwitchToUI();
            }
        }

        public void StartGame()
        {
            // [AUDIO-IP:G3] GameDataLoader가 연 곡 세션에 판정 타이밍을 붙인 뒤 채보를 초기화한다
            if (_songs != null) _session = _songs.Current;
            if (chartManager == null || _session == null || _judgement == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            _timing?.Dispose();
            _timing = GameplayTimingBinding.Attach(_judgement, _session, OnTimingAdvance, OnTimingLaneInput, IsGameRunningNow, OnSongPausedExternally);
            bgaController?.Follow(_session);

            chartManager.Init(chartData, this);
            scoreManager.Init(chartData.totalNotes);

            IsGameRunning = true;
        }
        
        public void SetBGAData(string videoFileName, Sprite backgroundArt)
        {
            bgaController?.Init(videoFileName, backgroundArt);
        }

        // 영상 준비 대기는 BGAController가 안다. 기다릴 것이 없으면 즉시 true.
        public UniTask<bool> WaitBGAReadyAsync(float timeoutSeconds, CancellationToken ct)
        {
            if (bgaController == null) return UniTask.FromResult(true);
            return bgaController.WaitPreparedAsync(timeoutSeconds, ct);
        }

        public void StartMusic(double delayTime)
        {
            // [AUDIO-IP:G4] 노트싱크 오프셋은 세션이 적용한다 (양수: 음악 늦게 시작, 음수: 음악 일찍 시작)
            int audioOffsetMs = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                audioOffsetMs = settingsManager.Current.audioOffsetMs;

            _session?.Start(delayTime, audioOffsetMs);
        }

        public double GetCurrentTime()
        {
            if (!IsGameRunning || _session == null) return 0f;
            // [AUDIO-IP:G5] 일시정지 중에는 세션이 곡 시계를 멈추므로 채보 시간도 고정된다
            // → TimelineController, NoteController 등 GetCurrentTime() 기반 위치 계산이 모두 멈춤
            return _session.Clock.Frame.SongTime;
        }

        // [AUDIO-IP:G6] 매 프레임 SyncTime은 JudgementDriver가 OnTimingAdvance로 대신한다

        private void OnApplicationFocus(bool focus)
        {
            if (!focus)
                Pause();
        }

        public void Pause()
        {
            if (!IsGameRunning || IsPaused) return;
            IsPaused = true;
            // [AUDIO-IP:G7] 입력 맵을 끄기 전에 곡 시계를 멈춘다(그 뒤 합성 release는 판정되지 않는다)
            _session?.Pause(PauseReason.User);
            _inputManager.SwitchToUI();
            if (ServiceLocator.TryGet<IUIManager>(out var uiManager))
            {
                // Overlay: 게임 화면 위에 겹쳐 표시. sortingOrder는 UIManager가 표시 깊이에 따라 부여(게임 레이어 위).
                uiManager.ShowUI<PauseUI>(PushMode.Overlay);
            }
        }

        public void Resume()
        {
            if (!IsGameRunning || !IsPaused) return;
            StartCoroutine(ResumeCountdownSequence());
        }

        private IEnumerator ResumeCountdownSequence()
        {
            _inputManager.SetInputActive(false); // 카운트다운 중 입력 차단
            if (clearEffectImage != null)
            {
                clearEffectImage.color = Color.white;
                clearEffectImage.gameObject.SetActive(true);
                for (int i = 3; i >= 1; i--)
                {
                    clearEffectImage.sprite = resumeCountdownSprites[i - 1];
                    yield return new WaitForSeconds(1f);
                }
                clearEffectImage.gameObject.SetActive(false);
            }

            // [AUDIO-IP:G8] 멈춘 곡 시각에서 다시 예약한다(BGA는 곡 시계를 따라 다시 재생된다)
            _session?.Resume();
            _inputManager.SetInputActive(true);
            _inputManager.SwitchToGameplay();
            IsPaused = false;
        }

        private void HandlePause() => Pause();

        public void SetChartData(ChartData data)
        {
            this.chartData = data;
            // ChartManager 초기화를 여기서 하는게 나을지도?
            // chartManager.Initialize(data); 
        }

        // ── 판정 타이밍 콜백 (GameplayTimingBinding) ── [AUDIO-IP:G9]
        // 진행: 마디 진행은 songTime, miss·홀드는 judgeTime으로 한다. [AUDIO-IP:G13]
        private void OnTimingAdvance(double songTime, double judgeTime)
        {
            chartManager.SyncTime(songTime, judgeTime);
        }

        // 입력: 판정 시각으로 판정한다. 판정할 수 없는 입력은 binding이 release만 넘기고,
        // 그 release(합성 release, 멈춘 동안의 release)는 홀드 상태만 푼다. [AUDIO-IP:G14]
        private void OnTimingLaneInput(in JudgedInput input)
        {
            if (!IsGameRunning) return;
            if (input.IsDown) chartManager.TryJudgeInput(input.Lane, input.JudgeTime);
            else chartManager.TryJudgeRelease(input.Lane, input.JudgeTime, input.Judgeable);
        }

        private bool IsGameRunningNow()
        {
            return IsGameRunning;
        }

        // [AUDIO-IP:G12] 포커스 상실·장치 변경·스트림 끊김으로 세션이 멈췄으면 일시정지 UI를 띄운다
        private void OnSongPausedExternally(PauseReason reason)
        {
            if (IsGameRunning && !IsPaused) Pause();
        }

        private void HandleRestart()
        {
            if (!IsGameRunning) return;
            SceneManager.LoadScene("GameScene");
        }


        // ── ChartManager 판정 결과 콜백 (IGameManager) ──
        // ChartManager.ApplyJudgment/CheckMissedNotes/TryJudge*가 호출.
        // 점수는 ScoreManager로, 연출은 *Event로 CharacterAnimator에 전달한다.
        public void OnNoteJudged(JudgeType judgeType, NotePosition pos, int groupID)
        {
            scoreManager.ProcessJudge(judgeType);
        }

        public void OnNoteMissed()
        {
            scoreManager.ProcessJudge(JudgeType.Umm);
        }

        public void OnHoldStart(NotePosition pos, int groupID)
        {
            OnHoldStartEvent?.Invoke(pos, groupID);
        }

        // 홀드가 끝났다. 키를 뗐거나, 본체를 완주했거나, 본체를 놓쳤거나 — 구독자는 구분하지 않는다.
        public void OnHoldStop(NotePosition pos, int groupID)
        {
            OnHoldStopEvent?.Invoke(pos, groupID);
        }

        public void OnLaneInput(LaneInputResult result)
        {
            OnLaneInputEvent?.Invoke(result);
        }



        public void UpdateScore(int score)
        {
            scoreText.text = score.ToString("D7");  // 7자리 숫자로 포맷 (0000000)
        }

        public void UpdateCombo(int combo)
        {
            // 숨기기 전에 텍스트를 먼저 갱신해야 다시 켜질 때 이전 값이 한 프레임 노출되지 않는다
            if (combo > 0)
            {
                comboText.text = combo.ToString();
            }

            // 콤보 0이면 Combo 하위(라벨/숫자/인디케이터)를 통째로 숨긴다
            if (comboRoot != null)
            {
                comboRoot.SetActive(combo > 0);
            }
            else
            {
                comboText.gameObject.SetActive(combo > 0);   // comboRoot 미할당 시 기존 동작으로 폴백
            }

            // 인디케이터 밀림 연출과 숫자 팝 연출. 반드시 SetActive 이후에 호출한다
            // (SetActive(true)가 OnEnable을 동기 실행해 위상/포즈를 리셋하므로, 먼저 부르면 이번 콤보가 지워진다)
            // OnComboChanged는 판정마다 같은 값·0으로도 재발행되므로 증가 판별은 각 컴포넌트가 한다
            comboIndicator?.SetCombo(combo);
            comboCountAnimator?.SetCombo(combo);
        }

        public void UpdateGauge(float percentage)
        {
            // 소수점 2자리까지 표시 (100.0%)
            gaugeText.text = $"{percentage:F2}%";
            
            if (gaugeBar != null)
            {
                gaugeBar.fillAmount = percentage / 100f;
            }
            
            // 색상 변경 로직 (선택사항)
            if (percentage >= 100f) gaugeText.color = Color.cyan; // Perfect/Master 유지 중
            else gaugeText.color = Color.white;
        }



        // 게임 종료 처리
        public void OnGameFinished()
        {
            IsGameRunning = false;

            // [AUDIO-IP:G11] 음악 정지
            _session?.Stop();

            // UI 모드로 전환
            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.SwitchToUI();
            }

            int finalScore = scoreManager.GetFinalScore();
            ClearType rank = scoreManager.GetClearRank();

            Debug.Log($"Game Finished. Score: {finalScore}, Rank: {rank}");

            // 클리어 연출 시퀀스 시작 (2초 후)
            StartCoroutine(ShowClearSequence(rank));
        }

        // 클리어 연출 표시 (즉시 등급 스프라이트 표시 후 4초 대기)
        private IEnumerator ShowClearSequence(ClearType rank)
        {
            // 클리어 등급 스프라이트 설정 및 즉시 표시. 등급별 색상은 스프라이트가 담당한다
            if (clearEffectImage != null && (int)rank >= 0 && (int)rank < clearTypeSprites.Length)
            {
                clearEffectImage.color = Color.white;
                clearEffectImage.sprite = clearTypeSprites[(int)rank];
                clearEffectImage.gameObject.SetActive(true);

                // 4초 표시
                yield return new WaitForSeconds(4f);
                clearEffectImage.gameObject.SetActive(false);
            }
            else if (clearEffectImage != null)
            {
                Debug.LogWarning($"[GameManager] clearTypeSprites에 {rank} 등급 스프라이트가 없습니다. 클리어 연출을 건너뜁니다.");
            }

            // BGA 정지 후 GameScene Canvas 비활성화 및 결과 화면 표시
            bgaController?.Stop();
            if (gameCanvas != null)
            {
                gameCanvas.gameObject.SetActive(false);
            }
            ShowResultScreen();
        }

        // 결과 화면 표시
        private void ShowResultScreen()
        {
            if (ServiceLocator.TryGet<IUIManager>(out var uiManager))
            {
                uiManager.ShowUI<ResultUI>().Init(
                    scoreManager.GetFinalScore(),
                    scoreManager.GetClearRank(),
                    scoreManager.GetMaxCombo(),
                    scoreManager.GetTotalNoteCount(),
                    scoreManager.GetJudgeCounts(),
                    scoreManager.GetGaugePercent()
                );
            }
        }

        // 캐시된 ChartData 반환 (다시하기용)
        public ChartData GetCachedChartData() => chartData;



    }
}
