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

        /// <summary>
        /// 홀드 본체가 판정됐다. HoldStart/Holding 노트마다 반복 발화된다.
        /// </summary>
        void OnHoldStart(NotePosition pos, int groupID);

        /// <summary>
        /// 이 레인의 홀드가 끝났다. 원인은 셋이고 구독자는 구분하지 않는다.
        ///   1. 플레이어가 키를 뗌
        ///   2. 홀드 본체를 완주함 (HoldEnd 노트 판정)
        ///   3. 홀드 본체를 놓침 (HoldEnd 또는 HoldRelease 노트 miss)
        /// 그래서 이름이 Release가 아니라 Stop이다. 키를 뗀 건 세 원인 중 하나일 뿐이다.
        /// </summary>
        void OnHoldStop(NotePosition pos, int groupID);

        /// <summary>
        /// 키를 눌렀을 때 1회 발행. 판정 예측까지 끝난 완결된 결과를 넘긴다.
        /// 선입력 재판정(flush) 경로에서는 발행하지 않는다 — "물리적 키 입력당 정확히 1회"가 불변식이다.
        /// </summary>
        void OnLaneInput(LaneInputResult result);

        // 캐릭터 애니메이터 구독용 이벤트.
        // 판정 이벤트는 두지 않는다 — 홀드 본체와 릴리즈 판정이 계속 발화돼 캐릭터에는 잡음이고,
        // 히트 등급은 OnLaneInputEvent가 예측값으로 싣고 온다.
        event Action<NotePosition, int> OnHoldStartEvent;
        event Action<NotePosition, int> OnHoldStopEvent;
        event Action<LaneInputResult> OnLaneInputEvent;

        void Pause();
        void Resume();

        void OnGameFinished();
    }
}