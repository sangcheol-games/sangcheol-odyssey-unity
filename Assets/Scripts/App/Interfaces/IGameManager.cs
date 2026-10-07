using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Rhythm;
using UnityEngine;


namespace SCOdyssey.App
{
    public interface IGameManager
    {
        void SetBGAData(string videoFileName, Sprite backgroundArt);

        /// <summary>
        /// SetBGAData가 시작한 영상 준비가 끝날 때까지 기다린다. 로딩 화면이 이 대기를 가린다.
        /// 기다릴 것이 없으면(BGA 없는 곡) 즉시 반환하고, 타임아웃하면 배경아트로 진행한다(false).
        /// </summary>
        UniTask<bool> WaitBGAReadyAsync(float timeoutSeconds, CancellationToken ct);

        void StartGame();
        bool IsGameRunning { get; }
        bool IsPaused { get; }
        bool IsAudioPlaying { get; }  // 오디오 재생 중인지 확인

        void SetChartData(ChartData chartData);
        ChartData GetCachedChartData();  // 캐시된 차트 데이터 반환 (다시하기용)

        void Pause();
        void Resume();

        void OnGameFinished();
    }
}
