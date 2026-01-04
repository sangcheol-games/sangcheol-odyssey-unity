using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Game;
using UnityEngine;

public class GameSceneTester : MonoBehaviour
{
    [Header("Test Settings")]
    public TextAsset testChartFile;
    public int testBpm = 160;
    public AudioClip testMusic;

    void Start()
    {
        IGameManager gameManager = null;
        if (!ServiceLocator.TryGet<IGameManager>(out gameManager))
        {
            Debug.LogError("IGameManager가 등록되지 않았습니다!");
            return;
        }
        
        if (gameManager == null)
        {
            Debug.LogError("GameManager가 씬에 없습니다!");
            return;
        }

        Debug.Log("--- [TEST MODE] Starting Game ---");

        if (testMusic != null)
        {
            gameManager.SetAudioClip(testMusic);
        }

        if (testChartFile != null)
        {
            ChartData data = ChartParser.Parse(testChartFile.text, testBpm);
            
            gameManager.SetChartData(data);
            
        }
        else
        {
            Debug.LogError("테스트할 채보 파일(TextAsset)을 할당해주세요.");
        }
    }
}