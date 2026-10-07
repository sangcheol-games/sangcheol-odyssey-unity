using System;

namespace SCOdyssey.Audio
{
    // 이번 프레임의 곡 시각 스냅샷. 판정선, 노트, BGA, 게임플레이가 모두 같은 값을 읽는다.
    public readonly struct SongFrame
    {
        public readonly double SongTime;        // Ready·Starting에서는 0
        public readonly long QpcTicks;          // 스냅샷을 만든 시각
        public readonly int Epoch;
        public readonly bool IsRunning;

        public SongFrame(double songTime, long qpcTicks, int epoch, bool isRunning)
        {
            SongTime = songTime;
            QpcTicks = qpcTicks;
            Epoch = epoch;
            IsRunning = isRunning;
        }
    }

    // 임의 QPC 시각의 곡 시각. 입력 판정에 쓴다(결정적, 스무딩 없음).
    public readonly struct SongTimePoint
    {
        public readonly double SongTime;
        public readonly int Epoch;
        public readonly bool IsRunning;         // 그 시각에 곡 시계가 흐르고 있었는지

        public SongTimePoint(double songTime, int epoch, bool isRunning)
        {
            SongTime = songTime;
            Epoch = epoch;
            IsRunning = isRunning;
        }
    }

    public enum DiscontinuityReason
    {
        Start,
        Pause,
        Resume,
        Reset,                  // 세대 변경, DEVICEREINITIALIZE
        Stop
    }

    public readonly struct ClockDiscontinuity
    {
        public readonly DiscontinuityReason Reason;
        public readonly int PreviousEpoch;
        public readonly int Epoch;
        public readonly double SongTime;

        public ClockDiscontinuity(DiscontinuityReason reason, int previousEpoch, int epoch, double songTime)
        {
            Reason = reason;
            PreviousEpoch = previousEpoch;
            Epoch = epoch;
            SongTime = songTime;
        }
    }

    public interface ISongClock
    {
        SongFrame Frame { get; }
        bool TrySongTimeAt(long qpcTicks, out SongTimePoint point);
        event Action<ClockDiscontinuity> Discontinuity;
    }
}
