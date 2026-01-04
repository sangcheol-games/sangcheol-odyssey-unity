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
            MusicSO music = MusicManager.Instance.GetCurrentMusic();
            if (music == null)
            {
                Debug.LogError("Null Exception: no selected music");
                yield break;
            }
            Debug.Log($"Loading Music: {music.title[Language.JP]}");

            // TODO: Load Resource(배경, 음악 등)

            yield return LoadChart(music);

        }

        private IEnumerator LoadChart(MusicSO music)
        {
            if (music.chartFile == null)
            {
                Debug.LogError("Null Exception: Chart file missing");
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

            ServiceLocator.Get<IGameManager>().SetChartData(parsedData);

            yield return null;
        }
    }
}

