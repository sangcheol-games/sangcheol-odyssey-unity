using SCOdyssey.Core;
using SCOdyssey.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        public AudioSource audioSource;
        public void SetAudioClip(AudioClip audioClip)     // GameDataLoader에서 MusicSO의 musicFile을 전달받아 설정
        {
            this.audioSource.clip = audioClip;
        }
        public ScoreManager scoreManager;
        public ChartManager chartManager;
        public ChartData chartData;


        [Header("게임 상태")]
        private double globalStartTime;
        public bool IsGameRunning { get; private set; } = false;

        [Header("UI")]
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI comboText;
        public TextMeshProUGUI gaugeText;
        public Image gaugeBar; // fillAmount로 게이지 바 표현 시


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
        }

        private void Start()
        {
            // InputManager는 Managers에서 이미 생성 및 등록됨
            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.SwitchToGameplay(); // 게임용 키 세팅으로 전환
                inputManager.OnLanePressed += HandleLaneInput;
                inputManager.OnLaneReleased += HandleLaneRelease;
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
                inputManager.SwitchToUI();
            }
        }

        public void StartGame()
        {
            if (chartManager == null || audioSource == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            chartManager.Init(chartData, this);
            scoreManager.Init(100); // TODO: chartData에서 노트 개수 받아오기

            globalStartTime = AudioSettings.dspTime;
            IsGameRunning = true;
        }

        public void StartMusic(double delayTime)
        {
            audioSource.PlayDelayed((float)delayTime);
        }

        public double GetCurrentTime()
        {
            if (!IsGameRunning) return 0f;
            return AudioSettings.dspTime - globalStartTime;
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


        public void OnNoteJudged(JudgeType judgeType)
        {
            scoreManager.ProcessJudge(judgeType);
        }

        public void OnNoteMissed()
        {
            scoreManager.ProcessJudge(JudgeType.Uhm);
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



        public void OnGameFinished()
        {
            IsGameRunning = false;
            int finalScore = scoreManager.GetFinalScore();

            Debug.Log($"Game Over. Final Score: {finalScore}");
            // TODO:결과창 호출 로직
        }

        // 캐시된 ChartData 반환 (다시하기용)
        public ChartData GetCachedChartData() => chartData;



    }
}
