using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace SCOdyssey.Audio
{
    public readonly struct AudioOutputRequest
    {
        public readonly AudioOutputKind Kind;
        public readonly Guid DeviceId;          // Guid.Empty = 기본 장치 따라가기(ASIO는 첫 드라이버)
        public readonly string DeviceName;      // GUID로 못 찾을 때 쓰는 보조 키
        public readonly int BufferLength;
        public readonly int BufferCount;

        public AudioOutputRequest(AudioOutputKind kind, Guid deviceId, string deviceName, int bufferLength, int bufferCount)
        {
            Kind = kind;
            DeviceId = deviceId;
            DeviceName = deviceName;
            BufferLength = bufferLength;
            BufferCount = bufferCount;
        }
    }

    public enum AudioApplyOutcome
    {
        Applied,                // 요청 구성으로 성공. 이때만 호출자가 설정을 저장한다
        AppliedWithFallback,    // 폴백 구성으로 동작 중
        Unchanged,
        Busy,                   // 다른 적용이 진행 중
        Rejected,               // 지금은 적용할 수 없음(곡 진행 중, 지원하지 않는 출력 타입 등)
        Failed
    }

    public readonly struct AudioApplyResult
    {
        public readonly AudioApplyOutcome Outcome;
        public readonly AudioOutputInfo Actual;
        public readonly string Message;

        public AudioApplyResult(AudioApplyOutcome outcome, AudioOutputInfo actual, string message)
        {
            Outcome = outcome;
            Actual = actual;
            Message = message;
        }
    }

    // 로비와 설정 화면에서만 쓴다. 적용은 항상 close→init이다.
    public interface IAudioOutputService
    {
        bool IsSupported(AudioOutputKind kind);
        IReadOnlyList<AudioDeviceInfo> GetCachedDevices(AudioOutputKind kind);
        UniTask<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(AudioOutputKind kind, bool refresh, CancellationToken ct);
        UniTask<AudioApplyResult> ApplyAsync(AudioOutputRequest request, CancellationToken ct);
    }
}
