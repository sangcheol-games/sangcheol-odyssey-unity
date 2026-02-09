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
            Debug.Log($"[GameDataLoader] Loading Music: {music.title[Language.JP]}");

            var gameManager = ServiceLocator.Get<IGameManager>();

            // 오디오 클립 로딩
            if (music.musicFile != null)
            {
                gameManager.SetAudioClip(music.musicFile);
            }
            else
            {
                Debug.LogWarning("[GameDataLoader] musicFile is null!");
            }

            // TODO: Load Resource(배경, 비디오 등)

            yield return LoadChart(music);

            // 모든 데이터 로딩 완료 후 게임 시작
            gameManager.StartGame();
        }

        private IEnumerator LoadChart(MusicSO music)
        {
            if (music.chartFile == null)
            {
                Debug.LogError("Null Exception: Chart file missing");
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

            string chartText = music.chartFile.text;
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

