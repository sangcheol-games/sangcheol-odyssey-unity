using SCOdyssey.Game;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;


namespace SCOdyssey.App
{
    public interface IGameManager
    {
        void SetAudioClip(AudioClip audioClip);    // GameDataLoader에서 MusicSO의 musicFile을 전달받아 설정

        void StartGame();
        double GetCurrentTime();
        bool IsGameRunning { get; }
        bool IsAudioPlaying { get; }  // 오디오 재생 중인지 확인

        void StartMusic(double delay);

        void SetChartData(ChartData chartData);
        ChartData GetCachedChartData();  // 캐시된 차트 데이터 반환 (다시하기용)

        void OnNoteJudged(JudgeType judgeType);
        void OnNoteMissed();

        void OnGameFinished();
    }
}