using System.Collections;
using System.Threading.Tasks;
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

            if (!ServiceLocator.TryGet<IAudioManager>(out var audioManager))
            {
                Debug.LogError("[GameDataLoader] IAudioManager not found in ServiceLocator!");
                yield break;
            }

            if (!ServiceLocator.TryGet<IContentProvider>(out var contentProvider))
            {
                Debug.LogError("[GameDataLoader] IContentProvider not found in ServiceLocator!");
                yield break;
            }

            // IContentProvider를 통해 오디오 로딩 (로컬/CDN 공통 인터페이스)
            Task<byte[]> audioTask = contentProvider.LoadAudioBytesAsync(music);
            yield return new WaitUntil(() => audioTask.IsCompleted);

            if (audioTask.Result != null)
            {
                // OPENMEMORY 방식으로 로드 (동기, 즉시 IsLoaded=true)
                audioManager.LoadAudioFromBytes(audioTask.Result);
            }
            else
            {
                Debug.LogWarning("[GameDataLoader] 오디오 데이터를 로드하지 못했습니다.");
            }

            // IContentProvider를 통해 BGA 경로 취득 (로컬: StreamingAssets, CDN: 복호화 캐시)
            Task<string> bgaTask = contentProvider.GetBGAPathAsync(music);
            yield return new WaitUntil(() => bgaTask.IsCompleted);

            gameManager.SetBGAData(bgaTask.Result, music.backgroundArt);

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

