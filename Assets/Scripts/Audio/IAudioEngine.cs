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
        bool IsRequestedConfig { get; }          // 마지막으로 요청한 구성(부팅 설정 또는 설정 화면 적용)으로 동작 중인지. false면 폴백 구성
        event Action<EngineStatus> StatusChanged;
    }
}
