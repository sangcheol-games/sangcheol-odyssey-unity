using SCOdyssey.Rhythm;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 카운트다운 텍스트 슬롯. (그룹 x 진행방향, 레인x)
    // countdownTexts 배열 인덱스와 1:1 대응.
    public enum CountdownSlot
    {
        TopLTR = 0,
        TopRTL = 1,
        BottomLTR = 2,
        BottomRTL = 3,
    };

    // Lane -> 그룹/포지션/카운트다운 슬롯 변환의 유일한 창구. 판정 층은 이 어휘를 모른다.
    public static class LaneLayout
    {
        // 어느 판정선/캐릭터 소속인가. 레인 1~2 = Top, 3~4 = Bottom
        public static LaneGroup GroupOf(Lane lane)
            => (int)lane < 2 ? LaneGroup.Top : LaneGroup.Bottom;

        // 그룹 내 위치. 첫 레인(짝수 인덱스) = Top, 둘째(홀수) = Bottom
        public static NotePosition PositionOf(Lane lane)
            => (int)lane % 2 == 0 ? NotePosition.Top : NotePosition.Bottom;

        // 그룹당 2슬롯. LTR이 앞
        public static CountdownSlot ToCountdownSlot(LaneGroup group, bool isLTR)
            => (CountdownSlot)(((int)group * 2) + (isLTR ? 0 : 1));
    }
}
