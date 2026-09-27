using System;

namespace SCOdyssey.Audio
{
    // Degraded: NOSOUND로만 부팅됨. Failed: System 생성 실패. 둘 다 재생 API는 no-op, 세션은 QPC 시계로 진행한다.
    public enum EngineStatus
    {
        Uninitialized,
        Starting,
        Running,
        Degraded,
        Failed,
        Disposed
    }

    public interface IAudioEngine
    {
        EngineStatus Status { get; }
        int Generation { get; }                 // System을 새로 init할 때마다 오른다
        AudioOutputInfo CurrentOutput { get; }
        event Action<EngineStatus> StatusChanged;
    }
}
