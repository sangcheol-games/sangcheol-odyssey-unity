using System;

namespace SCOdyssey.Audio
{
    // Ended 상태는 없다. 음원이 끝나도 Playing으로 남고(IsAudioFinished) 일시정지할 수 있다.
    public enum SongSessionState
    {
        Ready,
        Starting,
        LeadIn,
        Playing,
        Paused,
        Resuming,
        Recovering,
        Stopped,
        Disposed
    }

    public enum PauseReason
    {
        None,
        User,
        FocusLost,
        DeviceChanged,
        StreamStalled
    }

    public enum SongSessionEvent
    {
        Started,
        Paused,
        Resumed,
        AudioStarted,
        AudioEnded,
        Recovered,
        Stopped,
        Disposed
    }

    public interface ISongSession
    {
        SongSessionState State { get; }
        PauseReason PauseReason { get; }
        bool IsAudioFinished { get; }
        ISongClock Clock { get; }

        void Start(double leadInSeconds, int audioOffsetMs);   // 노트 싱크는 여기서만 래치한다
        void Pause(PauseReason reason);
        void Resume();
        void Stop();

        event Action<SongSessionEvent> Changed;
    }
}
