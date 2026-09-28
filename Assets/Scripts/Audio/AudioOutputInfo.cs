using System;

namespace SCOdyssey.Audio
{
    public enum AudioOutputKind
    {
        Wasapi,
        Asio,
        NoSound
    }

    // 엔진이 실제로 쓰고 있는 출력 구성. 요청값이 아니라 init 뒤 검증한 값이다.
    public readonly struct AudioOutputInfo
    {
        public readonly AudioOutputKind Kind;
        public readonly Guid DeviceId;          // Guid.Empty = 기본 장치 따라가기
        public readonly string DeviceName;
        public readonly int SampleRate;
        public readonly int BufferLength;
        public readonly int BufferCount;

        public AudioOutputInfo(AudioOutputKind kind, Guid deviceId, string deviceName, int sampleRate, int bufferLength, int bufferCount)
        {
            Kind = kind;
            DeviceId = deviceId;
            DeviceName = deviceName;
            SampleRate = sampleRate;
            BufferLength = bufferLength;
            BufferCount = bufferCount;
        }
    }

    public readonly struct AudioDeviceInfo
    {
        public readonly Guid Id;
        public readonly string Name;
        public readonly int SystemRate;

        public AudioDeviceInfo(Guid id, string name, int systemRate)
        {
            Id = id;
            Name = name;
            SystemRate = systemRate;
        }
    }
}
