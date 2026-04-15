using System;
using SCOdyssey.Game;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;


namespace SCOdyssey.App
{
    public interface IGameManager
    {
        void SetBGAData(string videoFileName, Sprite backgroundArt);

        void StartGame();
        double GetCurrentTime();
        bool IsGameRunning { get; }
        bool IsPaused { get; }
        bool IsAudioPlaying { get; }  // 오디오 재생 중인지 확인

        void StartMusic(double delay);

        void SetChartData(ChartData chartData);
        ChartData GetCachedChartData();  // 캐시된 차트 데이터 반환 (다시하기용)

        void OnNoteJudged(JudgeType judgeType, NotePosition pos, int groupID);
        void OnNoteMissed();
        void OnHoldStart(NotePosition pos, int groupID);
        void OnHoldEnd(NotePosition pos, int groupID);
        void OnHoldRelease(NotePosition pos, int groupID);
        void OnLaneInput(NotePosition pos, int groupID);

        // 캐릭터 애니메이터 구독용 이벤트
        event Action<JudgeType, NotePosition, int> OnNoteJudgedEvent;
        event Action<NotePosition, int> OnHoldStartEvent;
        event Action<NotePosition, int> OnHoldEndEvent;
        event Action<NotePosition, int> OnHoldReleaseEvent;
        event Action<NotePosition, int> OnLaneInputEvent;

        void Pause();
        void Resume();

        void OnGameFinished();
    }
}