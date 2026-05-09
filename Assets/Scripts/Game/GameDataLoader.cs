using System;
using System.Collections;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    internal static class CoroutineUtil
    {
        public static void WhenAll(
            MonoBehaviour runner,
            IEnumerator[] routines,
            Action onComplete
        )
        {
            if(routines == null || routines.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            int remaining = routines.Length;
            foreach(var routine in routines)
            {
                runner.StartCoroutine(
                    Track(routine, () =>
                    {
                        --remaining;
                        if(remaining == 0) onComplete?.Invoke();
                    })
                );
            }
        }

        private static IEnumerator Track(
            IEnumerator routine,
            Action onComplete
        )
        {
            yield return routine;
            onComplete();
        }
    }

    public class GameDataLoader : MonoBehaviour
    {
        private void Start()
        {
            var musicManager = ServiceLocator.Get<IMusicManager>();

            MusicSO music = musicManager.GetCurrentMusic();
            Difficulty difficulty = musicManager.GetCurrentDifficulty();

            if (music == null)
            {
                Debug.LogError($"[{nameof(GameDataLoader)}] No selected music!");
                return;
            }

            var gameManager = ServiceLocator.Get<IGameManager>();
            gameManager.SetBGAData(music.videoFileName, music.backgroundArt);

            // 캐시된 ChartData 확인 (다시하기용)
            ChartData chart = gameManager.GetCachedChartData() ?? LoadChart(music, difficulty);
            gameManager.SetChartData(chart);

            CoroutineUtil.WhenAll(this, new[]
            {
                LoadAudioData(music, initialDelay: chart.BarDuration)
            }, () => gameManager.StartGame());
        }

        private IEnumerator LoadAudioData(MusicSO music, double initialDelay)
        {
            var bgmAudioPath = music.audioFilePath;

            if (string.IsNullOrEmpty(bgmAudioPath))
            {
                Debug.LogWarning($"[{nameof(GameDataLoader)}] audioFilePath is empty!");
                yield return null;
            }
            Debug.Log($"[{nameof(GameDataLoader)}] Loading Music: {bgmAudioPath}");

            var audioManager = ServiceLocator.Get<FMODAudioManager2>();

            var session = AudioSession.New("Game")
                .ReserveBGM(bgmAudioPath, localTime: initialDelay)
                .Build();

            yield return audioManager.PushSessionAsync(session);
        }

        private ChartData LoadChart(MusicSO music, Difficulty difficulty)
        {
            if (!music.chartFile.TryGetValue(difficulty, out TextAsset chart) || chart == null)
            {
                Debug.LogError($"[{nameof(GameDataLoader)}] Chart file missing for difficulty: {difficulty}");
                return null;
            }

            string chartText = chart.text;
            int bpm = music.bpm;

            Debug.Log("Parsing Chart Data...");

            // TODO: 비동기처리 사용 여부 결정 (현재 동기)
            ChartData parsedData = ChartParser.Parse(chartText, bpm);

            if (parsedData == null)
            {
                Debug.LogError("[GameDataLoader] 파싱 실패!");
            }

            return parsedData;
        }
    }
}

