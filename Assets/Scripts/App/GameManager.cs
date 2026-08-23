using System;
using System.Collections;
using SCOdyssey.Core;
using SCOdyssey.Game;
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
    //  준비: Awake()에서 자신을 ServiceLocator에 등록하고 IAudioManager를 얻는다.
    //        Start()에서 IInputManager의 입력 이벤트와 ScoreManager의 UI 이벤트를 구독한다.
    //
    //  게임 시작: GameDataLoader가 StartGame()을 호출한다.
    //        scoreManager.Init() -> chartManager.Init() -> globalStartTime 기록(현재 DSP 시각) -> 입력 동기점 설정
    //
    //  시간: GetCurrentTime()은 (현재 DSP - globalStartTime), 즉 게임 상대시간을 돌려준다.
    //        일시정지 중에는 _pauseDspTime 기준으로 고정하고, 재개 시 흐른 만큼 globalStartTime을 보정한다.
    //        ※ FMOD DSP 클럭만 사용한다. AudioSettings.dspTime은 기준점이 달라 쓰지 않는다.
    //
    //  매 프레임: Update() -> chartManager.SyncTime(GetCurrentTime())
    //
    //  입력: HandleLaneInput()/HandleLaneRelease() -> chartManager.TryJudgeInput()/TryJudgeRelease()
    //        (입력 DSP 시각을 globalStartTime 기준 상대시간으로 변환해 전달한다)
    //
    //  판정 전파: JudgementBus를 소유만 하고 직접 중계하지는 않는다.
    //        ChartManager가 판정을, 여기가 키 입력을 발행하면 ScoreManager와 CharacterAnimator가 각자 구독해 받는다.
    //
    //  종료: 채보와 음원이 끝나면 ChartManager가 OnGameFinished()를 호출한다 -> 클리어 연출 -> ResultUI 표시.
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

        // 판정/입력 결과를 뿌리는 버스. ChartManager(판정)와 여기(입력)가 발행하고
        // ScoreManager·CharacterAnimator가 각자 구독한다.
        private readonly JudgementBus _judgementBus = new();


        [Header("게임 상태")]
        private double globalStartTime;  // 게임 상대시간의 원점(StartGame 시점의 DSP 시각)
        public bool IsGameRunning { get; private set; } = false;
        public bool IsPaused { get; private set; } = false;
        private double _pauseDspTime;
        public bool IsAudioPlaying => _audioManager != null && _audioManager.IsPlaying;

        [Header("UI")]
        public Canvas gameCanvas; // GameScene의 메인 Canvas (결과화면 표시 시 비활성화)
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI comboText;
        public TextMeshProUGUI gaugeText;
        public Image gaugeBar; // fillAmount로 게이지 바 표현 시
        public TextMeshProUGUI clearEffectText; // 클리어 연출 텍스트


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
            ServiceLocator.TryRegister<IJudgementBus>(_judgementBus);
            if (!ServiceLocator.TryGet<IAudioManager>(out _audioManager))
                Debug.LogError("[GameManager] IAudioManager not found in ServiceLocator!");

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

            scoreManager.OnScoreChanged += UpdateScore;
            scoreManager.OnComboChanged += UpdateCombo;
            scoreManager.OnGaugeChanged += UpdateGauge;
        }

        private void OnDestroy()
        {
            ServiceLocator.Remove<IGameManager>();
            ServiceLocator.Remove<IJudgementBus>();

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

            // 점수 구독을 먼저 붙인 뒤 채보를 준비한다
            scoreManager.Init(chartData.totalNotes, _judgementBus);
            chartManager.Init(chartData, this, _judgementBus);

            globalStartTime = _audioManager.GetDSPTime();

            // 동기점 기록: FMOD DSP 클럭(globalStartTime)과 OS 클럭을 같은 시점에 연속으로 읽어 변환 기준을 설정
            // AudioSettings.dspTime(Unity 내장)은 FMOD 클럭과 기준점이 다르므로 사용 금지
            _inputManager?.SetTimeSyncPoint(globalStartTime, Time.realtimeSinceStartupAsDouble);

            IsGameRunning = true;
        }
        
        public void SetBGAData(string videoFileName, Sprite backgroundArt)
        {
            bgaController?.Init(videoFileName, backgroundArt);
        }

        public void StartMusic(double delayTime)
        {
            // 노트싱크 오프셋 적용 (양수: 음악 늦게 시작, 음수: 음악 일찍 시작)
            double offsetSec = 0;
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager))
                offsetSec = settingsManager.Current.audioOffsetMs / 1000.0;

            double dspStartTime = _audioManager.GetDSPTime() + delayTime + offsetSec;
            _audioManager.PlayScheduled(dspStartTime);
            bgaController?.SchedulePlay(dspStartTime);
        }

        public double GetCurrentTime()
        {
            if (!IsGameRunning) return 0f;
            // 일시정지 중: DSP 클록이 계속 진행해도 채보 시간은 일시정지 시점으로 고정
            // → TimelineController, NoteController 등 GetCurrentTime() 기반 위치 계산이 모두 멈춤
            if (IsPaused) return _pauseDspTime - globalStartTime;
            return _audioManager.GetDSPTime() - globalStartTime;
        }


        private void Update()
        {
            if (!IsGameRunning || IsPaused) return;

            chartManager.SyncTime(GetCurrentTime());
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
            _pauseDspTime = _audioManager.GetDSPTime();
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
            if (clearEffectText != null)
            {
                clearEffectText.gameObject.SetActive(true);
                for (int i = 3; i >= 1; i--)
                {
                    clearEffectText.text = i.ToString();
                    clearEffectText.color = Color.white;
                    yield return new WaitForSeconds(1f);
                }
                clearEffectText.gameObject.SetActive(false);
            }

            // 일시정지 동안 흐른 DSP 시간만큼 globalStartTime을 보정하여 채보 위치를 유지
            globalStartTime += _audioManager.GetDSPTime() - _pauseDspTime;
            _audioManager.Resume();
            bgaController?.Resume();
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

        private void HandleLaneInput(Lane lane, double inputDspTime)
        {
            if (!IsGameRunning) return;

            var group = lane.GetGroup();
            // 판정 결과와 무관하게 입력 이벤트를 먼저 발화 (캐릭터 Y 이동 담당)
            _judgementBus.PublishLaneInput(GetNotePosition((int)lane), group);

            chartManager.TryJudgeInput(lane, inputDspTime - globalStartTime);
        }

        private void HandleLaneRelease(Lane lane, double inputDspTime)
        {
            if (!IsGameRunning) return;
            //Debug.Log($"Lane {lane} Released");

            var group = lane.GetGroup();
            // 키 릴리즈는 판정 성공 여부와 무관하게 홀드 상태 해제 신호로 사용
            _judgementBus.PublishHoldReleased(GetNotePosition((int)lane), group);

            chartManager.TryJudgeRelease(lane, inputDspTime - globalStartTime);
        }

        private static NotePosition GetNotePosition(int listIndex)
        {
            // 각 그룹 내 첫 번째 레인(짝수 인덱스) = Top, 두 번째(홀수) = Bottom
            return listIndex % 2 == 0 ? NotePosition.Top : NotePosition.Bottom;
        }

        private void HandleRestart()
        {
            if (!IsGameRunning) return;
            SceneManager.LoadScene("GameScene");
        }


        public void UpdateScore(int score)
        {
            scoreText.text = score.ToString("D7");  // 7자리 숫자로 포맷 (0000000)
        }

        public void UpdateCombo(int combo)
        {
            if (combo > 0)
            {
                comboText.text = combo.ToString();
                comboText.gameObject.SetActive(true);
            }
            else
            {
                comboText.gameObject.SetActive(false);
            }
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
            // 클리어 텍스트 설정 및 즉시 표시
            if (clearEffectText != null)
            {
                clearEffectText.gameObject.SetActive(true);

                switch (rank)
                {
                    case ClearType.AllPerfect:
                        clearEffectText.text = "ALL PERFECT";
                        clearEffectText.color = Color.cyan;
                        break;
                    case ClearType.OverMillion:
                        clearEffectText.text = "OVER MILLION";
                        clearEffectText.color = Color.yellow;
                        break;
                    case ClearType.FullCombo:
                        clearEffectText.text = "FULL COMBO";
                        clearEffectText.color = Color.green;
                        break;
                    case ClearType.Clear:
                        clearEffectText.text = "CLEAR";
                        clearEffectText.color = Color.white;
                        break;
                    case ClearType.Fail:
                        clearEffectText.text = "FAILED";
                        clearEffectText.color = Color.red;
                        break;
                }

                // 4초 표시
                yield return new WaitForSeconds(4f);
                clearEffectText.gameObject.SetActive(false);
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
