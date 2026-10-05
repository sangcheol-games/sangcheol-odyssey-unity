namespace SCOdyssey.Rhythm
{
    // 판정 층이 아는 유일한 레인 식별자. 그룹/포지션 같은 뷰 어휘는 LaneLayout이 파생한다.
    public enum Lane
    {
        L1 = 0,
        L2 = 1,
        L3 = 2,
        L4 = 3,
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
}
