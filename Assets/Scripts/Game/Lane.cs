namespace SCOdyssey.Game
{
    public enum LaneGroup
    {
        Top,
        Bottom,
    };

    public enum Lane
    {
        TopUpper = 0,
        TopLower = 1,
        BottomUpper = 2,
        BottomLower = 3,
    };

    // 채보 파일, Input System은 레인을 1~4로 세고, 내부에서는 0~3이다.
    public static class LaneMap
    {
        public const int FIRST_LANE = 1;   // 바깥 경계에서 쓰는 첫 레인 번호

        /// 채보 파일의 레인 자리 -> Lane
        public static Lane FromChartLine(int line) => (Lane)(line - FIRST_LANE);

        /// InputManager 생성자에 하드코딩된 1~4 -> Lane
        public static Lane FromInputIndex(int index) => (Lane)(index - FIRST_LANE);
    }

    public static class LaneExtensions
    {
        // 어느 판정선/캐릭터 소속인가. 레인 1~2 = Top, 3~4 = Bottom
        public static LaneGroup GetGroup(this Lane lane)
            => (int)lane < 2 ? LaneGroup.Top : LaneGroup.Bottom;
    };
}
