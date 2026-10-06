using System.Collections;
using SCOdyssey.Config;
using SCOdyssey.Core;
using SCOdyssey.Game;
using SCOdyssey.Rhythm;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // ── 흐름 (게임 오케스트레이터) ──────────────────────────────────────────
    //
    //  준비: Awake()에서 자신·버스·게임 시계(IRhythmClock)를 ServiceLocator에 등록하고 IAudioManager를 얻는다.
    //        Start()에서 IInputManager의 입력 이벤트를 구독하고 HudView를 ScoreManager에 연결한다.
    //
    //  게임 시작: GameDataLoader가 StartGame()을 호출한다.
    //        판정 세션 생성 -> scoreManager.Init() -> chartManager.Init() -> 시계 시작 -> 입력 동기점 설정 -> 음원 예약
    //
    //  시간: GameplayClock이 (현재 DSP - 원점), 즉 게임 상대시간을 낸다. 일시정지 중엔 멈추고 재개하면 이어진다.
    //        ※ FMOD DSP 클럭만 사용한다. AudioSettings.dspTime은 기준점이 달라 쓰지 않는다.
    //
    //  매 프레임: Update() -> session.Advance(now) -> chartManager.Tick(now) -> 종료 체크
    //
    //  입력: HandleLaneInput()/HandleLaneRelease() -> session.Press()/Release()
    //        (입력 DSP 시각을 게임 상대시간으로 바꿔 넘긴다)
    //
    //  판정 전파: RhythmSession이 버스에 키 입력과 판정을 발행하고,
    //        ScoreManager·CharacterAnimator·ChartManager(노트 뷰)가 각자 구독해 받는다.
    //
    //  종료: 모든 노트가 판정되고 음원이 끝나면 OnGameFinished() -> 클리어 연출 -> ResultUI 표시.
    //
    //  화면 표시는 직접 하지 않는다. 점수·콤보·게이지는 HudView가, 중앙 대형 텍스트는 GameBannerView가 맡고
    //  여기는 "언제 무엇을 띄울지"의 순서만 코루틴으로 쥔다.
    // ──────────────────────────────────────────────────────────────────────────
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        private IAudioManager _audioManager;
        private IInputManager _inputManager;
        public ScoreManager scoreManager;
        public ChartManager chartManager;
        public ChartData chartData;

        [Header("BGA")]
        public BGAController bgaController; // Inspector 연결 (없으면 BGA 비활성)

        [Header("판정 설정 (비우면 Resources/Config/JudgeSettings)")]
        [SerializeField] private JudgeSettingsSO judgeSettings;

        // 판정/입력 결과를 뿌리는 버스. RhythmSession이 발행하고
        // ScoreManager·CharacterAnimator·ChartManager가 각자 구독한다.
        private readonly JudgementBus _judgementBus = new();
        private GameplayClock _clock;
        private RhythmSession _session;


        [Header("게임 상태")]
        public bool IsGameRunning { get; private set; } = false;
        public bool IsPaused => _clock != null && _clock.IsPaused;
        public bool IsAudioPlaying => _audioManager != null && _audioManager.IsPlaying;

        [Header("UI")]
        public Canvas gameCanvas; // GameScene의 메인 Canvas (결과화면 표시 시 비활성화)
        public HudView hudView;             // 점수·콤보·게이지 (비어 있으면 같은 오브젝트에서 찾는다)
        public GameBannerView bannerView;   // 클리어 배너 + 재개 카운트다운


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
            ServiceLocator.TryRegister<IJudgementBus>(_judgementBus);

            if (hudView == null) hudView = GetComponent<HudView>();
            if (bannerView == null) bannerView = GetComponent<GameBannerView>();
            if (!ServiceLocator.TryGet<IAudioManager>(out _audioManager))
                Debug.LogError("[GameManager] IAudioManager not found in ServiceLocator!");

            _clock = new GameplayClock(() => _audioManager.GetDSPTime());
            ServiceLocator.TryRegister<IRhythmClock>(_clock);

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
                _inputManager.OnLanePressed += HandleLaneInput;
                _inputManager.OnLaneReleased += HandleLaneRelease;
                _inputManager.OnRestart += HandleRestart;
                _inputManager.OnPause += HandlePause;
            }
            else
            {
                Debug.LogError("[GameManager] IInputManager not found in ServiceLocator!");
            }

            hudView?.Bind(scoreManager);
        }

        private void OnDestroy()
        {
            ServiceLocator.Remove<IGameManager>();
            ServiceLocator.Remove<IJudgementBus>();
            ServiceLocator.Remove<IRhythmClock>();

            if (_inputManager != null)
            {
                _inputManager.OnLanePressed -= HandleLaneInput;
                _inputManager.OnLaneReleased -= HandleLaneRelease;
                _inputManager.OnRestart -= HandleRestart;
                _inputManager.OnPause -= HandlePause;
                _inputManager.SwitchToUI();
            }
        }

        public void StartGame()
        {
            if (chartManager == null || _audioManager == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            _session = CreateSession(chartData);

            // 점수 구독을 먼저 붙인 뒤 채보를 준비한다
            scoreManager.Init(chartData.totalNotes, _judgementBus);
            chartManager.Init(chartData, _session.Reader, _judgementBus);

            _clock.Start();

            // 동기점 기록: FMOD DSP 클럭(시계 원점)과 OS 클럭을 같은 시점에 연속으로 읽어 변환 기준을 설정
            // AudioSettings.dspTime(Unity 내장)은 FMOD 클럭과 기준점이 다르므로 사용 금지
            _inputManager?.SetTimeSyncPoint(_clock.OriginDsp, Time.realtimeSinceStartupAsDouble);

            // 0번(빈) 마디만큼 늦게 음원을 시작한다 → 그동안 1번 마디가 준비된다
            StartMusic(chartManager.BarDuration);

            IsGameRunning = true;
        }

        // 채보에서 판정 트랙을 만들어 엔진에 싣고, 버스로 발행하는 세션으로 감싼다
        private RhythmSession CreateSession(ChartData data)
        {
            double judgementOffsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                judgementOffsetSec = settingsManager.Current.judgmentOffset * 0.003;

            var trackReport = new ChartParseReport();
            JudgeNote[] judgeNotes = data.BuildJudgeTrack(trackReport);
            foreach (string error in trackReport.Errors) Debug.LogError(error);
            foreach (string warning in trackReport.Warnings) Debug.LogWarning(warning);
            JudgeSettings judgeSettingsInUse = JudgeSettingsSO.Resolve(judgeSettings).WithOffset(judgementOffsetSec);
            var engine = new JudgeEngine(judgeSettingsInUse);
            engine.Load(judgeNotes);
            Debug.Log($"[Judge] 윈도우 {judgeSettingsInUse.Windows}, 선택 {judgeSettingsInUse.Select}, 오프셋 {judgeSettingsInUse.OffsetSec * 1000:0.#}ms");
            LogJudgeTrackSummary(data, judgeNotes);

            return new RhythmSession(engine, _judgementBus);
        }

        // 판정 트랙 요약 로그(정렬·짝 검증은 JudgeEngine.Load가 한다)
        private static void LogJudgeTrackSummary(ChartData data, JudgeNote[] judgeNotes)
        {
            int viewNoteCount = 0;
            int bodyCount = 0;
            foreach (LaneData lane in data.GetFullChartList())
            {
                viewNoteCount += lane.Notes.Count;
                foreach (NoteData note in lane.Notes)
                    if (note.noteType == NoteType.Holding) bodyCount++;
            }

            var kindCount = new int[3];
            foreach (JudgeNote note in judgeNotes) kindCount[(int)note.Kind]++;

            Debug.Log(
                $"[JudgeTrack] {judgeNotes.Length}개 (채보 노트 {viewNoteCount}개 중 본체(3) {bodyCount}개 제외 / 총 노트 {data.totalNotes})\n" +
                $"  종류: Tap={kindCount[(int)NoteKind.Tap]}, " +
                $"HoldHead={kindCount[(int)NoteKind.HoldHead]}, " +
                $"HoldTail={kindCount[(int)NoteKind.HoldTail]}");
        }

        public void SetBGAData(string videoFileName, Sprite backgroundArt)
        {
            bgaController?.Init(videoFileName, backgroundArt);
        }

        // 시계 원점에서 delayTime 뒤에 음원과 BGA를 예약한다
        private void StartMusic(double delayTime)
        {
            // 노트싱크 오프셋 적용 (양수: 음악 늦게 시작, 음수: 음악 일찍 시작)
            double offsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                offsetSec = settingsManager.Current.audioOffsetMs / 1000.0;

            double dspStartTime = _clock.OriginDsp + delayTime + offsetSec;
            _audioManager.PlayScheduled(dspStartTime);
            bgaController?.SchedulePlay(dspStartTime);
        }

        public double GetCurrentTime() => _clock.Now;


        private void Update()
        {
            if (!IsGameRunning || IsPaused) return;

            double now = _clock.Now;
            _session.Advance(now);
            chartManager.Tick(now);

            if (_session.IsFinished && !IsAudioPlaying)
            {
                Debug.Log("Game Cleared.");
                OnGameFinished();
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus)
                Pause();
        }

        public void Pause()
        {
            if (!IsGameRunning || IsPaused) return;
            _clock.Pause();
            _audioManager.Pause();
            bgaController?.Pause();
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
            if (bannerView != null)
            {
                for (int i = 3; i >= 1; i--)
                {
                    bannerView.ShowCount(i);
                    yield return new WaitForSeconds(1f);
                }
                bannerView.Hide();
            }

            // 일시정지 동안 흐른 DSP 시간만큼 원점을 밀어 채보 위치를 유지
            _clock.Resume();
            _audioManager.Resume();
            bgaController?.Resume();
            _inputManager.SetInputActive(true);
            _inputManager.SwitchToGameplay();
        }

        private void HandlePause() => Pause();

        public void SetChartData(ChartData data)
        {
            this.chartData = data;
            // ChartManager 초기화를 여기서 하는게 나을지도?
            // chartManager.Initialize(data); 
        }

        private void HandleLaneInput(Lane lane, double inputDspTime)
        {
            if (!IsGameRunning) return;

            // 세션이 판정 전에 입력 이벤트부터 발행한다 (캐릭터 Y 이동 담당)
            _session.Press(lane, _clock.ToChartTime(inputDspTime));
        }

        private void HandleLaneRelease(Lane lane, double inputDspTime)
        {
            if (!IsGameRunning) return;

            // 키 릴리즈는 판정 성공 여부와 무관하게 홀드 상태 해제 신호로도 나간다
            _session.Release(lane, _clock.ToChartTime(inputDspTime));
        }

        private void HandleRestart()
        {
            if (!IsGameRunning) return;
            SceneManager.LoadScene("GameScene");
        }


        // 게임 종료 처리
        public void OnGameFinished()
        {
            IsGameRunning = false;
            _clock.Stop();

            // 음악 정지
            _audioManager?.Stop();

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

        // 클리어 연출 표시 (즉시 텍스트 표시 후 4초 대기)
        private IEnumerator ShowClearSequence(ClearType rank)
        {
            if (bannerView != null)
            {
                bannerView.ShowClear(rank);
                yield return new WaitForSeconds(4f);
                bannerView.Hide();
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
                    scoreManager.GetScoreRank(),
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
