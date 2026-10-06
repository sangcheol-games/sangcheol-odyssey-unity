using SCOdyssey.Rhythm;
using UnityEngine;

namespace SCOdyssey.Game
{
    // 플레이필드 좌표 공식. 마디는 좌우 끝점 사이를 비트 수만큼 균등 분할하고, LTR은 왼쪽에서, RTL은 오른쪽에서 시작한다
    public sealed class NoteGeometry
    {
        private readonly RectTransform _left;
        private readonly RectTransform _right;

        public NoteGeometry(RectTransform left, RectTransform right)
        {
            _left = left;
            _right = right;
        }

        public float LeftX => _left.anchoredPosition.x;
        public float RightX => _right.anchoredPosition.x;

        public float StartX(bool isLTR) => isLTR ? LeftX : RightX;
        public float EndX(bool isLTR) => isLTR ? RightX : LeftX;

        // 비트 1칸의 폭
        public float Interval(int beat) => (RightX - LeftX) / beat;

        public float NoteX(LaneData lane, int index) => StartX(lane.isLTR) + Interval(lane.beat) * index * (lane.isLTR ? 1 : -1);

        // 그 방향으로 가는 판정선의 도착점에 있는가
        public bool IsAtEnd(float x, bool isLTR) => Mathf.Approximately(x, EndX(isLTR));
    }
}
