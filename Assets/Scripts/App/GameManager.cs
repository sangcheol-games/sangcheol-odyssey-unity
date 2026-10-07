using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio;
using SCOdyssey.Config;
using SCOdyssey.Core;
using SCOdyssey.Game;
using SCOdyssey.Game.Timing;
using SCOdyssey.Rhythm;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // ── 흐름 (게임 오케스트레이터) ──────────────────────────────────────────
    //
    //  준비: Awake()에서 자신·버스·게임 시계(IRhythmClock)를 ServiceLocator에 등록하고 ISongPlayer와 JudgementDriver를 얻는다.
    //        타격음·타이밍 기록 구독도 여기서 붙인다(씬의 다른 구독자보다 먼저).
    //        Start()에서 IInputManager의 재시작·일시정지 이벤트를 구독하고 HudView를 ScoreManager에 연결한다.
    //
    //  게임 시작: GameDataLoader가 곡 세션을 연 뒤 StartGame()을 호출한다.
    //        판정 세션 생성(판정 상태 리더 등록) -> scoreManager.Init() -> playfield.Init()
    //        -> 게임 시계를 곡 세션에 연결 -> 판정 타이밍 연결(GameplayTimingBinding) -> 음원 예약(세션 Start)
    //
    //  시간: 곡 세션의 곡 시계가 유일한 시계다. SongRhythmClock이 곡 시각(Frame.SongTime)을 IRhythmClock으로 낸다.
    //        0은 게임 시작이고 음원은 리드인(빈 1마디) + 노트 싱크 뒤에 시작한다. 일시정지 중에는 세션이 곡 시계를 멈춘다.
    //
    //  매 프레임: JudgementDriver(-900)가 binding을 거쳐 OnTimingAdvance(songTime, judgeTime)
    //        -> session.Advance(judgeTime) -> playfield.Tick(songTime) -> 종료 체크
    //
    //  입력: JudgementDriver가 입력 시각을 곡 시각·판정 시각으로 바꿔 OnTimingLaneInput으로 넘긴다
    //        -> session.Press()/Release() (판정 싱크는 JudgementDriver가 judgeTime에 한 번만 적용한다)
    //
    //  판정 전파: RhythmSession이 버스에 키 입력과 판정을 발행하고,
    //        HitSoundPlayer·ScoreManager·CharacterAnimator·PlayfieldView(노트 뷰)가 각자 구독해 받는다.
    //
    //  종료: 모든 노트가 판정되고 음원이 끝나면 OnGameFinished() -> 최고기록 저장(IUserDataManager) -> 클리어 연출 -> ResultUI 표시.
    //
    //  화면 표시는 직접 하지 않는다. 점수·콤보·게이지는 HudView가, 중앙 대형 텍스트는 GameBannerView가 맡고
    //  여기는 "언제 무엇을 띄울지"의 순서만 코루틴으로 쥔다.
    // ──────────────────────────────────────────────────────────────────────────
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        private ISongPlayer _songs;
        private ISongSession _songSession;
        private JudgementDriver _judgement;
        private GameplayTimingBinding _timing;
        private IInputManager _inputManager;
        public ScoreManager scoreManager;
        [FormerlySerializedAs("chartManager")]
        public PlayfieldView playfield;
        public ChartData chartData;

        [Header("BGA")]
        public BGAController bgaController; // Inspector 연결 (없으면 BGA 비활성)

        [Header("판정 설정 (비우면 Resources/Config/JudgeSettings)")]
        [SerializeField] private JudgeSettingsSO judgeSettings;

        // 판정/입력 결과를 뿌리는 버스. RhythmSession이 발행하고
        // HitSoundPlayer·ScoreManager·CharacterAnimator·PlayfieldView가 각자 구독한다.
        private readonly JudgementBus _judgementBus = new();
        private readonly SongRhythmClock _rhythmClock = new();
        private RhythmSession _rhythm;
        private HitSoundPlayer _hitSounds;
        private JudgementTimingRecorder _timingRecorder;


        [Header("게임 상태")]
        public bool IsGameRunning { get; private set; } = false;
        public bool IsPaused { get; private set; } = false;
        // 음원이 끝나기 전까지 true. 음원이 끝난 뒤에도 세션은 일시정지할 수 있다.
        public bool IsAudioPlaying => _songSession != null && !_songSession.IsAudioFinished;

        [Header("UI")]
        public Canvas gameCanvas; // GameScene의 메인 Canvas (결과화면 표시 시 비활성화)
        public HudView hudView;             // 점수·콤보·게이지 (비어 있으면 같은 오브젝트에서 찾는다)
        public GameBannerView bannerView;   // 클리어 배너 + 재개 카운트다운


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
            ServiceLocator.TryRegister<IJudgementBus>(_judgementBus);
            ServiceLocator.TryRegister<IRhythmClock>(_rhythmClock);

            // 타격음은 씬의 다른 NoteJudged 구독자(CharacterAnimator·PlayfieldView)보다 먼저 받아야 하므로 여기서 붙인다
            ServiceLocator.TryGet<IOneShotPlayer>(out var oneShots);
            _hitSounds = new HitSoundPlayer(_judgementBus, oneShots);
            if (ServiceLocator.TryGet<IJudgementTimingLog>(out var timingLog))
                _timingRecorder = new JudgementTimingRecorder(_judgementBus, timingLog);

            if (hudView == null) hudView = GetComponent<HudView>();
            if (bannerView == null) bannerView = GetComponent<GameBannerView>();
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
                // 레인 입력은 JudgementDriver → binding으로 받는다(OnTimingLaneInput)
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
            // SwitchToUI가 만드는 합성 release보다 먼저 판정 연결을 끊는다
            _timing?.Dispose();
            _timing = null;
            _hitSounds?.Dispose();
            _hitSounds = null;
            _timingRecorder?.Dispose();
            _timingRecorder = null;
            _rhythmClock.Detach();

            ServiceLocator.Remove<IGameManager>();
            ServiceLocator.Remove<IJudgementBus>();
            ServiceLocator.Remove<IRhythmClock>();
            ServiceLocator.Remove<IJudgeStateReader>();

            if (_inputManager != null)
            {
                _inputManager.OnRestart -= HandleRestart;
                _inputManager.OnPause -= HandlePause;
                _inputManager.SwitchToUI();
            }
        }

        public void StartGame()
        {
            // GameDataLoader가 연 곡 세션에 판정 타이밍을 붙인 뒤 세션을 시작한다
            if (_songs != null) _songSession = _songs.Current;
            if (playfield == null || _songSession == null || _judgement == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            _rhythm = CreateSession(chartData);
            // 캐릭터가 판정선에 붙을 때 누르는 중인 홀드를 읽는다
            ServiceLocator.TryRegister<IJudgeStateReader>(_rhythm.Reader);

            // 점수 구독을 먼저 붙인 뒤 채보를 준비한다
            scoreManager.Init(chartData.totalNotes, _judgementBus);
            playfield.Init(chartData, _rhythm.Reader, _judgementBus);

            _rhythmClock.Attach(_songSession);

            // 세션을 시작하기 전에 붙여야 Started(노트 싱크 래치, 포커스 확인)를 놓치지 않는다
            _timing?.Dispose();
            _timing = GameplayTimingBinding.Attach(_judgement, _songSession, OnTimingAdvance, OnTimingLaneInput, IsGameRunningNow, OnSongPausedExternally);
            bgaController?.Follow(_songSession);

            // 채보는 1번 마디 시작이 음원 0초가 되게 쓰여 있다. 그래서 0번(빈) 마디만큼 늦게 음원을 시작한다
            StartMusic(playfield.BarDuration);

            IsGameRunning = true;
        }

        // 채보에서 판정 트랙을 만들어 엔진에 싣고, 버스로 발행하는 세션으로 감싼다.
        // 판정 싱크는 JudgementDriver가 judgeTime에 적용하므로 엔진 오프셋은 0이다
        private RhythmSession CreateSession(ChartData data)
        {
            var trackReport = new ChartParseReport();
            JudgeNote[] judgeNotes = data.BuildJudgeTrack(trackReport);
            foreach (string error in trackReport.Errors) Debug.LogError(error);
            foreach (string warning in trackReport.Warnings) Debug.LogWarning(warning);
            JudgeSettings judgeSettingsInUse = JudgeSettingsSO.Resolve(judgeSettings);
            var engine = new JudgeEngine(judgeSettingsInUse);
            engine.Load(judgeNotes);
            Debug.Log($"[Judge] 윈도우 {judgeSettingsInUse.Windows}, 선택 {judgeSettingsInUse.Select}");
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

        // 영상 준비 대기는 BGAController가 안다. 기다릴 것이 없으면 즉시 true.
        public UniTask<bool> WaitBGAReadyAsync(float timeoutSeconds, CancellationToken ct)
        {
            if (bgaController == null) return UniTask.FromResult(true);
            return bgaController.WaitPreparedAsync(timeoutSeconds, ct);
        }

        // 리드인 뒤에 음원을 시작하도록 세션을 시동한다
        private void StartMusic(double delayTime)
        {
            // 노트싱크 오프셋은 세션이 적용한다 (양수: 음악 늦게 시작, 음수: 음악 일찍 시작)
            int audioOffsetMs = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                audioOffsetMs = settingsManager.Current.audioOffsetMs;

            _songSession?.Start(delayTime, audioOffsetMs);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus)
                Pause();
        }

        public void Pause()
        {
            if (!IsGameRunning || IsPaused) return;
            IsPaused = true;
            // 입력 맵을 끄기 전에 곡 시계를 멈춘다(그 뒤 합성 release는 판정되지 않는다)
            _songSession?.Pause(PauseReason.User);
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
                PlayfieldSettingsSO settings = PlayfieldSettingsSO.Shared;
                for (int i = settings.resumeCountFrom; i >= 1; i--)
                {
                    bannerView.ShowCount(i);
                    yield return new WaitForSeconds(settings.resumeCountSec);
                }
                bannerView.Hide();
            }

            // 멈춘 곡 시각에서 다시 예약한다(BGA는 곡 시계를 따라 다시 재생된다)
            _songSession?.Resume();
            _inputManager.SetInputActive(true);
            _inputManager.SwitchToGameplay();
            IsPaused = false;
        }

        private void HandlePause() => Pause();

        public void SetChartData(ChartData data)
        {
            this.chartData = data;
        }

        // ── 판정 타이밍 콜백 (GameplayTimingBinding) ──
        // 진행: 마디 진행은 songTime, miss·홀드는 judgeTime으로 한다.
        // 일시정지 중에도 binding은 Advance를 넘기므로 여기서 거른다.
        private void OnTimingAdvance(double songTime, double judgeTime)
        {
            if (IsPaused) return;

            _rhythm.Advance(judgeTime);
            playfield.Tick(songTime);

            if (_rhythm.IsFinished && !IsAudioPlaying)
            {
                Debug.Log("Game Cleared.");
                OnGameFinished();
            }
        }

        // 입력: 판정 시각으로 판정한다. 판정할 수 없는 입력은 binding이 release만 넘기고,
        // 그 release(합성 release, 멈춘 동안의 release)는 입력 이벤트만 내보내 홀드 상태를 푼다.
        private void OnTimingLaneInput(in JudgedInput input)
        {
            if (!IsGameRunning) return;

            Lane lane = LaneMap.FromInputIndex(input.Lane);
            if (input.IsDown)
            {
                if (!_rhythm.Press(lane, input.JudgeTime).Hit) _hitSounds?.PlayWhiff();
            }
            else if (input.Judgeable)
            {
                _rhythm.Release(lane, input.JudgeTime);
            }
            else
            {
                _rhythm.ReleaseUnjudged(lane, input.JudgeTime);
            }
        }

        private bool IsGameRunningNow()
        {
            return IsGameRunning;
        }

        // 포커스 상실·장치 변경·스트림 끊김으로 세션이 멈췄으면 일시정지 UI를 띄운다
        private void OnSongPausedExternally(PauseReason reason)
        {
            if (IsGameRunning && !IsPaused) Pause();
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

            // 음악 정지
            _songSession?.Stop();

            // UI 모드로 전환
            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.SwitchToUI();
            }

            int finalScore = scoreManager.GetFinalScore();
            ClearType rank = scoreManager.GetClearRank();

            Debug.Log($"Game Finished. Score: {finalScore}, Rank: {rank}");

            bool isNewBestScore = SaveRecord(finalScore, rank);

            // 클리어 연출 시퀀스 시작
            StartCoroutine(ShowClearSequence(rank, isNewBestScore));
        }

        // 곡 + 난이도의 최고기록 갱신 (실패한 판도 기록한다. 클리어 타입은 Fail로 남는다)
        // 반환값: 점수가 이전 최고점수보다 높은지 (결과 화면 NEW RECORD 도장용). 기록이 없던 곡이면 이전 점수를 0으로 본다
        private bool SaveRecord(int finalScore, ClearType rank)
        {
            if (!ServiceLocator.TryGet<IUserDataManager>(out var userDataManager))
            {
                Debug.LogWarning("[GameManager] IUserDataManager not found. 기록을 저장하지 않습니다.");
                return false;
            }

            var musicManager = ServiceLocator.Get<IMusicManager>();
            var currentMusic = musicManager.GetCurrentMusic();
            if (currentMusic == null)
            {
                // GameScene을 곡 선택 없이 직접 연 경우
                Debug.LogWarning("[GameManager] 선택된 곡이 없어 기록을 저장하지 않습니다.");
                return false;
            }

            int musicId = currentMusic.id;
            Difficulty difficulty = musicManager.GetCurrentDifficulty();

            // SubmitResult는 콤보·비율 등 어느 항목이든 갱신되면 true라, 점수 신기록은 저장 전에 따로 비교한다
            int previousBestScore = 0;
            if (userDataManager.TryGetRecord(musicId, difficulty, out var previousRecord))
            {
                previousBestScore = previousRecord.bestScore;
            }
            bool isNewBestScore = finalScore > previousBestScore;

            bool isNewRecord = userDataManager.SubmitResult(
                musicId,
                difficulty,
                finalScore,
                scoreManager.GetMaxCombo(),
                scoreManager.GetGaugePercent(),
                rank);

            if (isNewRecord)
            {
                Debug.Log($"[GameManager] 기록 갱신: music {musicId} {difficulty}");
            }

            return isNewBestScore;
        }

        // 클리어 연출 표시 (즉시 배너 표시 후 설정한 시간만큼 대기)
        private IEnumerator ShowClearSequence(ClearType rank, bool isNewBestScore)
        {
            if (bannerView != null)
            {
                bannerView.ShowClear(rank);
                yield return new WaitForSeconds(PlayfieldSettingsSO.Shared.clearBannerSec);
                bannerView.Hide();
            }

            // BGA 정지 후 GameScene Canvas 비활성화 및 결과 화면 표시
            bgaController?.Stop();
            if (gameCanvas != null)
            {
                gameCanvas.gameObject.SetActive(false);
            }
            ShowResultScreen(isNewBestScore);
        }

        // 결과 화면 표시
        private void ShowResultScreen(bool isNewBestScore)
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
                    scoreManager.GetGaugePercent(),
                    isNewBestScore
                );
            }
        }

        // 캐시된 ChartData 반환 (다시하기용)
        public ChartData GetCachedChartData() => chartData;
    }
}
