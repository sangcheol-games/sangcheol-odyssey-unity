using SCOdyssey.Core;
using SCOdyssey.Game;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    public class GameManager : MonoBehaviour, IGameManager
    {
        [Header("참조")]
        public AudioSource audioSource;
        public void SetAudioClip(AudioClip audioClip)     // 테스트용 임시필드. MusicManager 구현 후 제거 예정
        {
            this.audioSource.clip = audioClip;
        }
        public ChartManager chartManager;
        public ChartData chartData;


        [Header("게임 상태")]
        private float globalStartTime;
        public bool IsGameRunning { get; private set; } = false;

        [Header("점수 및 콤보")]
        private float score = 0;
        private int combo = 0;


        private void Awake()
        {
            ServiceLocator.TryRegister<IGameManager>(this);
        }

        private void Start()
        {
            var inputManager = new InputManager();
            inputManager.Enable();
            ServiceLocator.TryRegister<IInputManager>(inputManager);

            inputManager.SwitchToGameplay(); // 게임용 키 세팅으로 전환
            inputManager.OnLanePressed += HandleLaneInput;

            // 여기가 실제 코드. 위가 테스트 코드
            /*
            if (ServiceLocator.TryGet<IInputManager>(out var inputManager))
            {
                inputManager.SwitchToGameplay(); // 게임용 키 세팅으로 전환
                inputManager.OnLanePressed += HandleLaneInput;
            }
            */


            // TODO : chart Load
            StartGame();
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

        private void StartGame()
        {
            if (chartManager == null || audioSource == null || chartData == null)
            {
                Debug.LogError("GameManager 초기화 실패!");
                return;
            }

            chartManager.Init(chartData, this);

            globalStartTime = (float)AudioSettings.dspTime;
            IsGameRunning = true;
        }
        
        public void StartMusic(float delayTime)
        {
            audioSource.PlayDelayed(delayTime);
        }

        public float GetCurrentTime()
        {
            if (!IsGameRunning) return 0f;
            return (float)(AudioSettings.dspTime - globalStartTime);
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


        public void OnNoteJudged(JudgeType judgeType)
        {
            combo++;

            // TODO: 점수 계산 로직 개선
            float addScore = (judgeType == JudgeType.Perfect) ? 100 : 50;
            score += addScore;

            Debug.Log($"Hit! Score: {score}, Combo: {combo}");
        }

        public void OnNoteMissed()
        {
            combo = 0;
            //Debug.Log("Miss! Combo Reset");
        }

        public void OnGameFinished()
        {
            IsGameRunning = false;
            Debug.Log($"Game Over. Final Score: {score}");
        }



    }
}
