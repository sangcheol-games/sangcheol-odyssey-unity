using System.Collections;
using Cysharp.Threading.Tasks;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // GameScene 진입 시 선택된 곡의 오디오/BGA/채보를 로딩한 뒤 GameManager.StartGame()으로 게임을 시작시키는 부트로더.
    // 채보는 ChartParser.Parse로 변환 후 GameManager에 캐싱 → 다시하기 시 재파싱 없이 캐시를 재사용한다.
    // 곡은 ISongPlayer로 연다(세션은 이 씬이 언로드될 때 Dispose된다). 음원을 열지 못하면 로비로 돌아간다.
    // 음원 경로가 비어 있는 곡은 무음 세션으로 진행한다(곡 시계만 흐른다).
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

            // 곡 세션 로딩
            if (!ServiceLocator.TryGet<ISongPlayer>(out var songs))
            {
                Debug.LogError("[GameDataLoader] ISongPlayer not found in ServiceLocator!");
                yield break;
            }

            // BGA Prepare와 오디오 로딩을 병렬 시작 (Prepare가 오래 걸리는 영상도 충분한 시간 확보)
            gameManager.SetBGAData(music.videoFileName, music.backgroundArt);

            if (!string.IsNullOrEmpty(music.audioFilePath))
            {
                // NONBLOCKING 로드 완료까지 대기 (보통 몇 프레임, 최대 10초)
                SongLoadResult result = default;
                yield return songs.LoadAsync(music.audioFilePath, this.GetCancellationTokenOnDestroy()).ToCoroutine(r => result = r);
                if (!result.Ok)
                {
                    if (result.Status == AudioLoadStatus.Cancelled || result.Status == AudioLoadStatus.Superseded) yield break;
                    // TODO: 공용 알림 UI가 생기면 로비에서 안내한다.
                    Debug.LogWarning("[GameDataLoader] 곡 음원을 열지 못해 로비로 돌아갑니다(" + result.Status + "): " + result.Detail);
                    SceneManager.LoadScene("MainScene");
                    yield break;
                }
            }
            else
            {
                Debug.LogWarning("[GameDataLoader] audioFilePath is empty! 소리 없이 진행합니다.");
                songs.CreateSilent();
            }

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

            // 캐시된 ChartData 확인 (다시하기용): 있으면 재파싱 없이 그대로 재사용 → ChartManager.Init에서 즉시 시작
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

