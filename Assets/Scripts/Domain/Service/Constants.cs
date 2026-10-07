namespace SCOdyssey.Domain.Service
{
    public static class Constants
    {
        public const int LANE_GROUP_COUNT = 2;
        public const int LANE_COUNT = 4;   // 레인 수
        public const int COUNTDOWN_SLOT_COUNT = LANE_GROUP_COUNT * 2;   // 그룹 x 진행방향(LTR/RTL). countdownImages 배열 크기


        public enum Difficulty
        {
            Easy,
            Normal,
            Hard,
            Extreme
        }

        // 노트 표시 상태. Hidden=숨김, Ghost=반투명(다음 마디 예고), Active=불투명. 판정 가능 여부와는 무관(판정은 엔진 윈도우가 정한다)
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
            Holding = 3,        // 홀드 본체: 판정 대상 아님(다음 마디로 홀드가 이어짐을 표시)
            HoldEnd = 4,        // 홀드 꼬리: 떼는 타이밍 판정, 헤드 비표시
            HoldRelease = 5     // 홀드 꼬리: 떼는 타이밍 판정, 헤드 표시
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
    }
}
