using System.Collections;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class GameDataLoader : MonoBehaviour
    {

        private void Start()
        {
            StartCoroutine(LoadGameData());
        }

        private IEnumerator LoadGameData()
        {
            if (!ServiceLocator.TryGet<IMusicManager>(out var musicManager))
            {
                Debug.LogError("[GameDataLoader] IMusicManager not found in ServiceLocator!");
                yield break;
            }

            MusicSO music = musicManager.GetCurrentMusic();
            if (music == null)
            {
                Debug.LogError("[GameDataLoader] No selected music!");
                yield break;
            }
            Debug.Log($"[GameDataLoader] Loading Music: {music.name}");

            var gameManager = ServiceLocator.Get<IGameManager>();

            // FMOD 오디오 로딩
            if (!ServiceLocator.TryGet<IAudioManager>(out var audioManager))
            {
                Debug.LogError("[GameDataLoader] IAudioManager not found in ServiceLocator!");
                yield break;
            }

            if (!string.IsNullOrEmpty(music.audioFilePath))
            {
                audioManager.LoadAudio(music.audioFilePath);
                // NONBLOCKING 로드 완료까지 대기 (보통 1-3프레임)
                while (!audioManager.IsLoaded) yield return null;
            }
            else
            {
                Debug.LogWarning("[GameDataLoader] audioFilePath is empty!");
            }

            // BGA 및 배경아트 로딩
            gameManager.SetBGAData(music.videoFileName, music.backgroundArt);

            yield return LoadChart(music);

            // 모든 데이터 로딩 완료 후 게임 시작
            gameManager.StartGame();
        }

        private IEnumerator LoadChart(MusicSO music)
        {
            var musicManager = ServiceLocator.Get<IMusicManager>();
            Difficulty difficulty = musicManager.GetCurrentDifficulty();

            if (!music.chartFile.TryGetValue(difficulty, out TextAsset chart) || chart == null)
            {
                Debug.LogError($"[GameDataLoader] Chart file missing for difficulty: {difficulty}");
                yield break;
            }

            var gameManager = ServiceLocator.Get<IGameManager>();

            // 캐시된 ChartData 확인 (다시하기용)
            ChartData cachedData = gameManager.GetCachedChartData();
            if (cachedData != null)
            {
                Debug.Log("Using cached ChartData for retry");
                gameManager.SetChartData(cachedData);
                yield break;
            }

            string chartText = chart.text;
            int bpm = music.bpm;

            Debug.Log("Parsing Chart Data...");

            // TODO: 비동기처리 사용 여부 결정 (현재 동기)
            ChartData parsedData = ChartParser.Parse(chartText, bpm);

            if (parsedData == null)
            {
                Debug.LogError("[GameDataLoader] 파싱 실패!");
                yield break;
            }

            gameManager.SetChartData(parsedData);

            yield return null;
        }
    }
}

