using System;
using System.Collections.Generic;

namespace SCOdyssey.Audio.Engine
{
    internal readonly struct BootAttempt
    {
        public readonly AudioOutputKind Kind;
        public readonly Guid DeviceId;
        public readonly string DeviceName;
        public readonly int BufferLength;
        public readonly int BufferCount;
        public readonly string Label;

        public BootAttempt(AudioOutputKind kind, Guid deviceId, string deviceName, int bufferLength, int bufferCount, string label)
        {
            Kind = kind;
            DeviceId = deviceId;
            DeviceName = deviceName;
            BufferLength = bufferLength;
            BufferCount = bufferCount;
            Label = label;
        }

        public bool SameConfig(BootAttempt other)
        {
            return Kind == other.Kind && DeviceId == other.DeviceId && DeviceName == other.DeviceName
                && BufferLength == other.BufferLength && BufferCount == other.BufferCount;
        }
    }

    // 부팅 폴백 순서: 요청 구성 → WASAPI 기본 장치 512×4 → NOSOUND.
    // 안전 모드(-sco-audio-safe)면 요청 구성을 건너뛴다. ASIO를 쓸 수 없는 환경이면 ASIO 요청도 건너뛴다.
    // 부팅 폴백은 저장값을 바꾸지 않는다.
    internal static class BootPlan
    {
        public const int SafeBufferLength = 512;
        public const int SafeBufferCount = 4;

        public static List<BootAttempt> Build(AudioOutputRequest requested, bool safeMode, bool asioAllowed)
        {
            var attempts = new List<BootAttempt>(3);

            bool skipRequested = safeMode;
            if (requested.Kind == AudioOutputKind.Asio && !asioAllowed) skipRequested = true;
            if (!skipRequested)
            {
                attempts.Add(new BootAttempt(requested.Kind, requested.DeviceId, requested.DeviceName,
                    requested.BufferLength, requested.BufferCount, "요청 구성"));
            }

            var wasapiDefault = new BootAttempt(AudioOutputKind.Wasapi, Guid.Empty, "", SafeBufferLength, SafeBufferCount, "WASAPI 기본 장치 512x4");
            if (attempts.Count == 0 || !attempts[0].SameConfig(wasapiDefault)) attempts.Add(wasapiDefault);

            attempts.Add(new BootAttempt(AudioOutputKind.NoSound, Guid.Empty, "", SafeBufferLength, SafeBufferCount, "NOSOUND"));
            return attempts;
        }
    }
}
