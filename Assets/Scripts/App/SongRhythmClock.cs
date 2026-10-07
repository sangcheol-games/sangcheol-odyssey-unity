using SCOdyssey.Audio;
using SCOdyssey.Rhythm;

namespace SCOdyssey.App
{
    // 곡 세션의 곡 시각(Frame.SongTime)을 게임 시계(IRhythmClock)로 낸다. 세션이 없으면 0.
    // 일시정지·정지 때는 세션이 곡 시계를 멈추므로 여기서 따로 막지 않는다(막으면 클리어 배너 동안 판정선이 튄다).
    public sealed class SongRhythmClock : IRhythmClock
    {
        private ISongSession _session;

        public double Now
        {
            get
            {
                if (_session == null) return 0.0;
                return _session.Clock.Frame.SongTime;
            }
        }

        public void Attach(ISongSession session)
        {
            _session = session;
        }

        public void Detach()
        {
            _session = null;
        }
    }
}
