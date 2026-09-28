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

        public static readonly BootAttempt WasapiDefault = new BootAttempt(AudioOutputKind.Wasapi, Guid.Empty, "", SafeBufferLength, SafeBufferCount, "WASAPI 기본 장치 512x4");
        public static readonly BootAttempt NoSound = new BootAttempt(AudioOutputKind.NoSound, Guid.Empty, "", SafeBufferLength, SafeBufferCount, "NOSOUND");

        public static List<BootAttempt> Build(AudioOutputRequest requested, bool safeMode, bool asioAllowed)
        {
            var preferred = new List<BootAttempt>(1);
            bool skipRequested = safeMode;
            if (requested.Kind == AudioOutputKind.Asio && !asioAllowed) skipRequested = true;
            if (!skipRequested) preferred.Add(FromRequest(requested, "요청 구성"));
            return Chain(preferred);
        }

        // 설정 적용 폴백: 요청 구성 → 직전에 동작하던 구성(있으면) → WASAPI 기본 512×4 → NOSOUND.
        public static List<BootAttempt> BuildApply(AudioOutputRequest requested, BootAttempt previous, bool hasPrevious)
        {
            var preferred = new List<BootAttempt>(2);
            preferred.Add(FromRequest(requested, "요청 구성"));
            if (hasPrevious) preferred.Add(Relabel(previous, "직전 구성"));
            return Chain(preferred);
        }

        // 장치 사건 복구: (먼저 시도할 구성) → 직전 구성 → WASAPI 기본 512×4 → NOSOUND.
        public static List<BootAttempt> BuildRecovery(BootAttempt first, BootAttempt previous)
        {
            var preferred = new List<BootAttempt>(2);
            preferred.Add(first);
            preferred.Add(Relabel(previous, "직전 구성"));
            return Chain(preferred);
        }

        public static BootAttempt FromRequest(AudioOutputRequest request, string label)
        {
            return new BootAttempt(request.Kind, request.DeviceId, request.DeviceName, request.BufferLength, request.BufferCount, label);
        }

        // 같은 구성은 한 번만 넣고, 끝에 WASAPI 기본과 NOSOUND를 붙인다.
        private static List<BootAttempt> Chain(List<BootAttempt> preferred)
        {
            var attempts = new List<BootAttempt>(preferred.Count + 2);
            for (int i = 0; i < preferred.Count; i++) AddUnique(attempts, preferred[i]);
            AddUnique(attempts, WasapiDefault);
            AddUnique(attempts, NoSound);
            return attempts;
        }

        private static void AddUnique(List<BootAttempt> attempts, BootAttempt attempt)
        {
            for (int i = 0; i < attempts.Count; i++)
            {
                if (attempts[i].SameConfig(attempt)) return;
            }
            attempts.Add(attempt);
        }

        private static BootAttempt Relabel(BootAttempt attempt, string label)
        {
            return new BootAttempt(attempt.Kind, attempt.DeviceId, attempt.DeviceName, attempt.BufferLength, attempt.BufferCount, label);
        }
    }
}
