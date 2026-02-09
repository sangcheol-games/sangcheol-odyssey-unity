using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SCOdyssey.Game.Test
{
    /// <summary>
    /// MainScene에서 곡 선택 테스트를 위한 스크립트
    /// TODO: 실제 곡 선택 UI 구현 후 제거
    /// </summary>
    public class MusicSelectTester : MonoBehaviour
    {
        [Header("Test Settings")]
        [Tooltip("테스트할 곡 (MusicSO)")]
        public MusicSO testMusicSO;

        [Tooltip("자동으로 곡 선택 및 게임 시작")]
        public bool autoStart = true;

        [Tooltip("자동 시작 딜레이 (초)")]
        public float autoStartDelay = 1f;

        private void Start()
        {
            if (autoStart)
            {
                Invoke(nameof(SelectAndStartGame), autoStartDelay);
            }
        }

        private void Update()
        {
            // 스페이스바로 수동 시작
            if (Input.GetKeyDown(KeyCode.Space))
            {
                SelectAndStartGame();
            }
        }

        /// <summary>
        /// 곡을 선택하고 GameScene으로 전환
        /// </summary>
        public void SelectAndStartGame()
        {
            if (testMusicSO == null)
            {
                Debug.LogError("[MusicSelectTester] testMusicSO is null! Please assign a MusicSO in Inspector.");
                return;
            }

            // MusicManager에서 곡 선택
            if (ServiceLocator.TryGet<IMusicManager>(out var musicManager))
            {
                musicManager.SelectMusic(testMusicSO);
                Debug.Log($"[MusicSelectTester] Selected music: {testMusicSO.title[Domain.Service.Constants.Language.KR]}");

                // GameScene으로 전환
                SceneManager.LoadScene("GameScene");
            }
            else
            {
                Debug.LogError("[MusicSelectTester] IMusicManager not found! Make sure Managers object exists in MainScene.");
            }
        }
    }
}
