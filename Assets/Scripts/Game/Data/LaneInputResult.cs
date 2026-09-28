using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    /// <summary>
    /// 한 번의 레인 입력이 판정 대상을 잡았는지에 대한 분류.
    /// ChartManager가 판정을 만들지 않고 빠져나가는 세 경로에 1:1로 대응한다.
    /// </summary>
    public enum PressResult
    {
        /// 칠 노트를 잡았다. Judge가 그 예측 등급이다.
        HitTarget,

        /// 칠 노트가 없거나 판정 윈도우 밖이다. 헛침.
        NoTarget,

        /// 큐 맨 앞이 홀드 본체(Holding/HoldEnd/HoldRelease)라 누르는 판정 대상이 아니다.
        /// 홀드를 놓쳤다 다시 잡는 정상적인 플레이에서 나온다 — 헛침도 히트도 아니다.
        HoldBody,
    }

    /// <summary>
    /// 물리적 키 입력 1회의 완결된 결과. ChartManager가 판정 예측까지 끝낸 뒤 한 번만 발행한다.
    /// 같은 폴더의 ChartData·LaneData·NoteData처럼 게임플레이 계층이 주고받는 순수 데이터다.
    /// IGameManager 시그니처에 노출되지만 발행자(ChartManager)와 구독자(CharacterAnimator) 모두
    /// SCOdyssey.Game이라 여기에 둔다.
    ///
    /// ★ 불변식: 이 페이로드는 물리적 키 입력당 정확히 한 번 발행된다.
    ///   선입력 재판정(flush) 경로에서는 발행하지 않는다 — 소리도 연출도 누른 순간에 이미 끝났다.
    /// </summary>
    public readonly struct LaneInputResult
    {
        /// 그룹 내 위치. ChartManager는 Top 또는 Bottom만 발행한다(Middle은 파생값).
        public readonly NotePosition Pos;

        /// 판정선 그룹 ID. 캐릭터는 자기 그룹 것만 처리한다.
        public readonly int GroupID;

        /// 예측 판정 등급. Result가 HitTarget이 아니면 의미 없다(타격음용 값이 그대로 들어온다).
        public readonly JudgeType Judge;

        /// 이 입력이 무엇을 잡았는지.
        public readonly PressResult Result;

        /// 입력 시각(게임 상대시간, 초). 상하 동시 입력을 짝지을 때 시각 차이를 본다.
        public readonly double Time;

        public LaneInputResult(NotePosition pos, int groupID, JudgeType judge, PressResult result, double time)
        {
            Pos = pos;
            GroupID = groupID;
            Judge = judge;
            Result = result;
            Time = time;
        }
    }
}
