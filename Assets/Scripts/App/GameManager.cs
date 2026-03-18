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
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        private IAudioManager _audioManager;
        public ScoreManager scoreManager;
        public ChartManager chartManager;
        public ChartData chartData;

        [Header("BGA")]
        public BGAController bgaController; // Inspector 연결 (없으면 BGA 비활성)


        [Header("게임 상태")]
        private double globalStartTime;
        public bool IsGameRunning { get; private set; } = false;
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
            if (!ServiceLocator.TryGet<IAudioManager>(out _audioManager))
                Debug.LogError("[GameManager] IAudioManager not found in ServiceLocator!");
        }

        private void Start()
        {
            // InputManager는 Managers에서 이미 생성 및 등록됨
            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.SwitchToGameplay(); // 게임용 키 세팅으로 전환
                inputManager.OnLanePressed += HandleLaneInput;
                inputManager.OnLaneReleased += HandleLaneRelease;
                inputManager.OnRestart += HandleRestart;
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

            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.OnLanePressed -= HandleLaneInput;
                inputManager.OnLaneReleased -= HandleLaneRelease;
                inputManager.OnRestart -= HandleRestart;
                inputManager.SwitchToUI();
            }
        }

        public void StartGame()
        {
            if (chartManager == null || _audioManager == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            chartManager.Init(chartData, this);
            scoreManager.Init(chartData.totalNotes);

            globalStartTime = _audioManager.GetDSPTime();
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
            return _audioManager.GetDSPTime() - globalStartTime;
        }


        private void Update()
        {
            if (!IsGameRunning) return;

            chartManager.SyncTime(GetCurrentTime());

        }

        public void SetChartData(ChartData data)
        {
            this.chartData = data;
            // ChartManager 초기화를 여기서 하는게 나을지도?
            // chartManager.Initialize(data); 
        }

        private void HandleLaneInput(int laneIndex)
        {
            if (!IsGameRunning) return;
            chartManager.TryJudgeInput(laneIndex);
        }

        private void HandleLaneRelease(int laneIndex)
        {
            if (!IsGameRunning) return;
            //Debug.Log($"Lane {laneIndex} Released");
            chartManager.TryJudgeRelease(laneIndex);
        }

        private void HandleRestart()
        {
            if (!IsGameRunning) return;
            SceneManager.LoadScene("GameScene");
        }


        public void OnNoteJudged(JudgeType judgeType)
        {
            scoreManager.ProcessJudge(judgeType);
        }

        public void OnNoteMissed()
        {
            scoreManager.ProcessJudge(JudgeType.Umm);
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
