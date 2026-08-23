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

        void OnNoteJudged(JudgeType judgeType, NotePosition pos, LaneGroup group);
        void OnNoteMissed();
        void OnHoldStart(NotePosition pos, LaneGroup group);
        void OnHoldEnd(NotePosition pos, LaneGroup group);
        void OnHoldRelease(NotePosition pos, LaneGroup group);
        void OnLaneInput(NotePosition pos, LaneGroup group);

        // 캐릭터 애니메이터 구독용 이벤트
        event Action<JudgeType, NotePosition, LaneGroup> OnNoteJudgedEvent;
        event Action<NotePosition, LaneGroup> OnHoldStartEvent;
        event Action<NotePosition, LaneGroup> OnHoldEndEvent;
        event Action<NotePosition, LaneGroup> OnHoldReleaseEvent;
        event Action<NotePosition, LaneGroup> OnLaneInputEvent;

        void Pause();
        void Resume();

        void OnGameFinished();
    }
}