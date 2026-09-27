using System.Collections.Generic;

namespace SCOdyssey.Game.Timing
{
    public readonly struct LaneInputEvent
    {
        public readonly int Lane;               // 1~4
        public readonly bool IsDown;
        public readonly long QpcTicks;
        public readonly bool IsSynthetic;       // 입력 맵 비활성화·포커스 상실로 생긴 cancel

        public LaneInputEvent(int lane, bool isDown, long qpcTicks, bool isSynthetic)
        {
            Lane = lane;
            IsDown = isDown;
            QpcTicks = qpcTicks;
            IsSynthetic = isSynthetic;
        }
    }

    public interface IInputTimestampSource
    {
        // 쌓인 입력을 into 뒤에 붙이고 비운다. 붙인 개수를 돌려준다.
        int Drain(List<LaneInputEvent> into);
    }
}
