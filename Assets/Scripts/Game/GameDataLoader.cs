using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // GameScene 진입 로딩 전체를 주도하는 부트로더.
    // 로딩 화면 표시 → 오디오/BGA/채보 준비 → 최소 표시 시간 → 페이드아웃 → 시작 전 대기 → StartGame → 곡 시계 커밋 대기.
    //
    // 순서에서 중요한 두 가지:
    //  1. StartGame()은 로딩 화면이 걷히고 시작 전 대기까지 끝난 뒤에 부른다. StartGame이 곡 시계를
    //     시동시키므로 먼저 부르면 화면이 가려진 동안 리드인(빈 1마디)이 소모돼 첫 노트를 놓친다.
    //  2. 시작 전 대기 동안은 게임 화면이 보이지만 IsGameRunning이 false라 레인 입력·ESC·포커스 처리는 무시된다.
    //     StartGame 뒤 FMOD 커밋 전까지는 songTime이 0에 고정되는데(보통 몇 프레임, 최악 3초),
    //     이때는 화면이 이미 보이는 상태라 리드인 마디 위에서 잠깐 멈춰 보일 수 있다.
    //
    // 오디오 레이어가 UniTask 기반이라 이 파이프라인도 UniTask로 쓴다. 코루틴으로 쓰면 ToCoroutine 브리지가
    // 생기고, 무엇보다 yield를 가로지르는 try/catch를 못 써 아래 실패 처리를 한곳에 모을 수 없다.
    //
    // 곡은 ISongPlayer로 연다(세션은 이 씬이 언로드될 때 Dispose된다).
    // 음원 경로가 비어 있는 곡은 무음 세션으로 진행한다(곡 시계만 흐른다).
    // 입력 차단/복구는 GameLoadingUI가 표시/종료와 짝으로 소유하므로 여기서는 건드리지 않는다.
    public class GameDataLoader : MonoBehaviour
    {
        // 로딩이 순식간에 끝나도 이만큼은 곡 정보를 보여준다.
        private const float MinVisibleSeconds = 3.0f;

        // 로딩 화면이 걷힌 뒤 게임 화면을 보여주고 StartGame까지 기다리는 시간.
        private const float StartDelaySeconds = 0.5f;

        // BGA prepare 대기 한도. 초과하면 이번 곡은 배경아트로 진행한다.
        private const float BgaPrepareTimeoutSeconds = 3f;

        // 곡 세션 커밋 대기 한도. FmodSongSession.StartTimeoutSeconds(3.0) + 여유.
        private const float CommitTimeoutSeconds = 3.5f;

        // 로딩 화면이 걷히는 시간.
        private const float FadeOutSeconds = 0.30f;

        private enum CommitResult { Running, Paused, Dead, Timeout }

        private void Start()
        {
            LoadGameDataAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid LoadGameDataAsync(CancellationToken ct)
        {
            if (!ServiceLocator.TryGet<IUIManager>(out var uiManager))
            {
                Debug.LogError("[GameDataLoader] IUIManager를 찾지 못했습니다.");
                return;
            }
            ServiceLocator.TryGet<IMusicManager>(out var musicManager);

            // musicManager가 없으면 아래 try 안에서 FailToLobby로 안내한다.
            // 여기서는 로딩 화면을 먼저 띄우기 위한 값만 준비한다.
            MusicSO music = null;
            Difficulty difficulty = Difficulty.Easy;
            if (musicManager != null)
            {
                music = musicManager.GetCurrentMusic();
                difficulty = musicManager.GetCurrentDifficulty();
            }

            GameLoadingUI screen = null;
            try
            {
                // GameScene의 첫 렌더보다 먼저 덮는다(Start는 렌더 이전이고, floor는 sceneLoaded에서 이미 잡혔다).
                screen = uiManager.ShowUI<GameLoadingUI>(PushMode.Overlay);
                screen.BeginLoading(music, difficulty);

                float shownAt = Time.realtimeSinceStartup;

                if (musicManager == null) { screen.FailToLobby("곡 목록을 불러올 수 없습니다."); return; }
                if (music == null) { screen.FailToLobby("선택된 곡이 없습니다."); return; }
                Debug.Log($"[GameDataLoader] Loading Music: {music.name}");

                if (!ServiceLocator.TryGet<IGameManager>(out var gameManager))
                {
                    screen.FailToLobby("게임을 시작할 수 없습니다.");
                    return;
                }
                if (!ServiceLocator.TryGet<ISongPlayer>(out var songs))
                {
                    screen.FailToLobby("오디오 모듈을 사용할 수 없습니다.");
                    return;
                }

                // BGA Prepare와 오디오 로딩을 병렬 시작 (Prepare가 오래 걸리는 영상도 충분한 시간 확보)
                gameManager.SetBGAData(music.videoFileName, music.backgroundArt);

                screen.SetProgress(0.1f, "음원을 여는 중...");
                if (!string.IsNullOrEmpty(music.audioFilePath))
                {
                    // NONBLOCKING 로드 완료까지 대기 (보통 몇 프레임, 최대 10초)
                    SongLoadResult result = await songs.LoadAsync(music.audioFilePath, ct);
                    if (!result.Ok)
                    {
                        Debug.LogWarning("[GameDataLoader] " + result.Status + ": " + result.Detail);
                        screen.FailToLobby("곡 음원을 열지 못했습니다(" + result.Status + ").");
                        return;
                    }
                }
                else
                {
                    Debug.LogWarning("[GameDataLoader] audioFilePath is empty! 소리 없이 진행합니다.");
                    songs.CreateSilent();
                }

                // 영상 준비를 여기서 기다린다. 기다리지 않으면 295MB짜리 BGA가 플레이 도중 갑자기 켜지면서
                // 비키프레임 seek 히치가 난다. BGA가 없는 곡(대부분)은 즉시 반환된다.
                screen.SetProgress(0.55f, "배경을 준비하는 중...");
                bool bgaReady = await gameManager.WaitBGAReadyAsync(BgaPrepareTimeoutSeconds, ct);
                if (!bgaReady) Debug.Log("[GameDataLoader] 영상 준비가 늦어 배경아트로 진행합니다.");

                screen.SetProgress(0.8f, "채보를 준비하는 중...");
                if (!TryLoadChart(music, difficulty, gameManager, out string chartError))
                {
                    screen.FailToLobby(chartError);
                    return;
                }

                screen.SetProgress(0.95f, "거의 다 됐어요...");

                // ★ StartGame보다 먼저 최소 표시 시간을 소진한다(리드인 손실 방지).
                // 남은 시간을 진행률로 채운다 — 안 그러면 바가 95%에 멈춰 있는 것처럼 보인다(문구는 그대로 둔다).
                while (true)
                {
                    float shownFor = Time.realtimeSinceStartup - shownAt;
                    if (shownFor >= MinVisibleSeconds) break;

                    screen.SetProgress(Mathf.Lerp(0.95f, 1f, shownFor / MinVisibleSeconds), null);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                screen.SetProgress(1f, "시작!");
                await screen.FadeOutAndCloseAsync(FadeOutSeconds, ct);

                // 닫힌 로딩 화면은 더 이상 쓰지 않는다. 이후 실패는 아래 catch가 곧장 로비로 보낸다.
                screen = null;

                // 게임 화면을 잠깐 보여준 뒤 시작한다. 곡 시계는 StartGame에서 시동되므로 이 대기는 리드인을 소모하지 않는다.
                await UniTask.Delay(TimeSpan.FromSeconds(StartDelaySeconds), DelayType.Realtime, cancellationToken: ct);

                gameManager.StartGame();

                CommitResult commit = await WaitSongClockRunningAsync(songs, ct);

                // 포커스를 잃은 채 StartGame되면 GameplayTimingBinding이 세션을 즉시 FocusLost로 멈춘다.
                // 그 시점엔 IsGameRunning이 아직 false라 GameManager의 복구 경로가 PauseUI를 띄우지 못한다.
                if (commit != CommitResult.Running) gameManager.Pause();
            }
            catch (OperationCanceledException)
            {
                // 씬 언로드로 이 오브젝트가 파괴됐다. 로딩 화면도 그 전환과 함께 정리된다.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (screen != null) screen.FailToLobby("로딩 중 오류가 발생했습니다.");
                else SceneManager.LoadScene("MainScene");   // 로딩 화면을 띄우지 못했거나 이미 닫은 경우
            }
        }

        /// <summary>
        /// 곡 시계가 실제로 흐르기 시작할 때까지 기다린다.
        /// 이벤트로는 잡을 수 없다 — FmodSongSession.Commit()은 LeadIn/Playing 전이에 이벤트를 내지 않고,
        /// Started는 커밋 "전" Start()에서 나간다.
        /// </summary>
        private async UniTask<CommitResult> WaitSongClockRunningAsync(ISongPlayer songs, CancellationToken ct)
        {
            if (songs == null) return CommitResult.Dead;

            ISongSession session = songs.Current;
            if (session == null) return CommitResult.Dead;

            float startedAt = Time.realtimeSinceStartup;
            while (!session.Clock.Frame.IsRunning)
            {
                if (session.State == SongSessionState.Paused) return CommitResult.Paused;
                if (session.State == SongSessionState.Stopped || session.State == SongSessionState.Disposed)
                    return CommitResult.Dead;

                if (Time.realtimeSinceStartup - startedAt > CommitTimeoutSeconds)
                {
                    Debug.LogWarning($"[GameDataLoader] 곡 시계가 {CommitTimeoutSeconds}초 안에 시작되지 않았습니다.");
                    return CommitResult.Timeout;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            return CommitResult.Running;
        }

        /// <summary>
        /// 채보를 파싱해 GameManager에 넣는다. ChartParser.Parse는 동기이고 텍스트가 수 KB라 프레임을 나누지 않는다.
        /// </summary>
        private bool TryLoadChart(MusicSO music, Difficulty difficulty, IGameManager gameManager, out string error)
        {
            error = null;

            if (!music.chartFile.TryGetValue(difficulty, out TextAsset chart) || chart == null)
            {
                error = $"채보 파일이 없습니다: {difficulty}";
                return false;
            }

            // 캐시된 ChartData 확인 (다시하기용): 있으면 재파싱 없이 그대로 재사용 → ChartManager.Init에서 즉시 시작
            ChartData cachedData = gameManager.GetCachedChartData();
            if (cachedData != null)
            {
                Debug.Log("Using cached ChartData for retry");
                gameManager.SetChartData(cachedData);
                return true;
            }

            Debug.Log("Parsing Chart Data...");
            ChartData parsedData = ChartParser.Parse(chart.text, music.bpm);
            if (parsedData == null)
            {
                error = "채보 파싱에 실패했습니다.";
                return false;
            }

            gameManager.SetChartData(parsedData);
            return true;
        }
    }
}
