using SCOdyssey.Game;
using UnityEngine;

namespace SCOdyssey.App
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }
        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        [Header("참조")]
        public ChartManager chartManager;
        public AudioSource musicSource;
        public ChartData chartData;


        [Header("게임 상태")]
        private float musicStartTime;
        private bool isGameRunning = false;

        [Header("점수 및 콤보")]
        private float score = 0;
        private int combo = 0;


        private void Start()
        {
            // TODO : chart Load
            StartGame();
        }

        public void StartGame()
        {
            if (chartManager == null || musicSource == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            musicSource.Play();
            musicStartTime = (float)AudioSettings.dspTime;
            isGameRunning = true;

            chartManager.Init(chartData);
        }
        
        public float GetCurrentTime()
        {
            if (!isGameRunning) return 0f;
            return (float)(AudioSettings.dspTime - musicStartTime);
        }


        private void FixedUpdate()
        {
            if (!isGameRunning) return;

            chartManager.SyncTime(GetCurrentTime());
            // TODO: HandleInput with new Input System
            HandleInput();

        }

        public void SetChartData(ChartData data)
        {
            this.chartData = data;
            // ChartManager 초기화를 여기서 하는게 나을지도?
            // chartManager.Initialize(data); 
        }

        private void HandleInput()
        {
            // TODO: Input System으로 변경 필요
            if (Input.GetKeyDown(KeyCode.Q)) chartManager.TryJudgeInput(1);
            if (Input.GetKeyDown(KeyCode.A)) chartManager.TryJudgeInput(2);
            if (Input.GetKeyDown(KeyCode.K)) chartManager.TryJudgeInput(3);
            if (Input.GetKeyDown(KeyCode.M)) chartManager.TryJudgeInput(4);
        }


        public void OnNoteJudged(float timeDifference)
        {
            combo++;
            // TODO: 점수 계산 로직 개선
            if (timeDifference < 0.05f) score += 100;
            else if (timeDifference < 0.1f) score += 50;
            else score += 10;

            Debug.Log($"Hit! Score: {score}, Combo: {combo}");
        }

        public void OnNoteMissed()
        {
            combo = 0;
            //Debug.Log("Miss! Combo Reset");
        }

        public void OnGameFinished()
        {
            isGameRunning = false;
            Debug.Log($"Game Over. Final Score: {score}");
        }



    }
}
