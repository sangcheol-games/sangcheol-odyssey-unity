
namespace SCOdyssey.Domain.Service
{
    public static class Constants
    {
        public const int LANE_COUNT = 4;   // 레인 수. ChartManager의 _lanes/countdownImages 배열 크기


        // 판정 윈도우(초, 판정타이밍 기준 ±오차). ChartManager.GetJudgeType/CheckMissedNotes 등이 사용
        public const float JUDGE_PERFECT = 0.021f;
        public const float JUDGE_MASTER = 0.042f;
        public const float JUDGE_IDEAL = 0.084f;
        public const float JUDGE_KIND = 0.105f;
        public const float JUDGE_UMM = 0.126f;   // 판정 범위 최대치. 이 값을 넘어가면 miss(Umm)


        public enum Difficulty
        {
            Easy,
            Normal,
            Hard,
            Extreme
        }

        // 노트 표시/상호작용 상태. Hidden=숨김·판정X, Ghost=반투명·판정X(다음 마디 예고), Active=불투명·판정O
        public enum NoteState
        {
            Hidden,
            Ghost,
            Active
        }

        public enum NoteType
        {
            None = 0,
            Normal = 1,
            HoldStart = 2,
            Holding = 3,
            HoldEnd = 4,        // 끝점 플래그: 시각 없음, 누르고 있는지 판정
            HoldRelease = 5     // 릴리즈 판정: 헤드만 표시, 손을 떼는 판정 담당
        }

        public enum JudgeType
        {
            Perfect,
            Master,
            Ideal,
            Kind,
            Umm
        }

        public enum ClearType
        {
            Fail,           // 점수 < 700,000 (게이지 < 70%)
            Clear,          // 클리어 (기본)
            FullCombo,      // Uhm (miss) = 0
            OverMillion,    // Perfect + Master = Total
            AllPerfect      // Perfect = Total
        }

        public enum ScoreRank
        {
            SSS,    // 115만점 이상
            SS,     // 100만점 이상
            S,      // 97만점 이상
            A,      // 90만점 이상
            B,      // 80만점 이상
            C,      // 70만점 이상
            F       // 70만점 미만
        }

        // 그룹 내 노트 위치. 레인 1(짝수 인덱스)=Top, 레인 2(홀수)=Bottom, 상하 동시 홀드 시 Middle 파생
        public enum NotePosition
        {
            Top,
            Middle,
            Bottom
        }

        // CharacterState는 연출 전용 개념이라 Assets/Scripts/Game/CharacterState.cs로 옮겼다.
        // 참조자가 전부 SCOdyssey.Game이고, Spine 애니메이션 이름과 1:1로 묶이는 값이다.

    }
}
