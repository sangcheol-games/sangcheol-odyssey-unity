using SCOdyssey.Game;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;


namespace SCOdyssey.App
{
    public interface IGameManager
    {
        void SetAudioClip(AudioClip audioClip);    // 테스트용 임시필드. MusicManager 구현 후 제거 예정

        double GetCurrentTime();
        bool IsGameRunning { get; }

        void StartMusic(double delay);

        void SetChartData(ChartData chartData);

        void OnNoteJudged(JudgeType judgeType);
        void OnNoteMissed();

        void OnGameFinished();
    }
}