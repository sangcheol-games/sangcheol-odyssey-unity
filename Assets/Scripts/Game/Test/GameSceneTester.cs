using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Game;
using UnityEngine;

public class GameSceneTester : MonoBehaviour
{
    [Header("Test Settings")]
    public TextAsset testChartFile;
    public int testBpm = 160;
    public string testAudioPath;   // StreamingAssets 기준 상대 경로 (예: "Music/test.ogg")

    void Start()
    {
        if (!ServiceLocator.TryGet<IGameManager>(out var gameManager))
        {
            Debug.LogError("IGameManager가 등록되지 않았습니다!");
            return;
        }

        Debug.Log("--- [TEST MODE] Starting Game ---");

        if (!string.IsNullOrEmpty(testAudioPath) &&
            ServiceLocator.TryGet<FMODAudioManager2>(out var audioManager))
        {
            var session = AudioSession.New("Test Mode")
                .DefineSFX(testAudioPath)
                .Build();
            audioManager.PushSession(session);
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