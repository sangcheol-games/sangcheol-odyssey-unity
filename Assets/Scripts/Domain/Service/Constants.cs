
namespace SCOdyssey.Domain.Service
{
    public static class Constants
    {
        public const float JUDGE_PERFECT = 0.021f;
        public const float JUDGE_MASTER = 0.042f;
        public const float JUDGE_IDEAL = 0.084f;
        public const float JUDGE_KIND = 0.105f;
        public const float JUDGE_UMM = 0.126f;


        public enum Difficulty
        {
            Easy,
            Normal,
            Hard,
            Extreme
        }

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

        public enum NotePosition
        {
            Top,
            Middle,
            Bottom
        }

        public enum CharacterState
        {
            Idle,
            Hit0, Hit1, Hit2, Hit3,
            Top, Middle, Bottom,
            TopHold, MiddleHold, BottomHold,
            TopHitWhileBottomHold,              // 아래 홀드 중 위 히트 (bottomY 유지)
            BottomHitWhileTopHold,              // 위 홀드 중 아래 히트 (topY 유지)
            Attack,                             // 같은 레인 재입력 (Y 유지)
            Hit_Kind,                           // Kind 판정 히트
            Hit_Umm                             // Umm 판정 히트
        }

    }
}
