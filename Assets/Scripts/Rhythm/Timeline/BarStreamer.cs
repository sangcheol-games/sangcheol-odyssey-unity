using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 마디 순으로 정렬된 채보 레인을 앞에서부터 마디 단위로 내준다
    public sealed class BarStreamer
    {
        private readonly Queue<LaneData> _remaining;

        public BarStreamer(IEnumerable<LaneData> lanes)
        {
            _remaining = new Queue<LaneData>(lanes);
        }

        public bool IsDone => _remaining.Count == 0;

        // bar 이하 마디의 레인을 모두 꺼내 into에 담는다
        public void TakeUpTo(int bar, ICollection<LaneData> into)
        {
            while (_remaining.Count > 0 && _remaining.Peek().bar <= bar)
                into.Add(_remaining.Dequeue());
        }
    }
}
