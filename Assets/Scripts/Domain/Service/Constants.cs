
namespace SCOdyssey.Domain.Service
{
    public static class Constants
    {
        public const float JUDGE_PERFECT = 0.021f;
        public const float JUDGE_MASTER = 0.042f;
        public const float JUDGE_IDEAL = 0.084f;
        public const float JUDGE_KIND = 0.105f;
        public const float JUDGE_UHM = 0.126f;


        public enum Language
        {
            JP,
            KR,
            EN
        }

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
            HoldEnd = 4

        }

        public enum JudgeType
        {
            Perfect,
            Master,
            Ideal,
            Kind,
            Uhm
        }
    }
}
