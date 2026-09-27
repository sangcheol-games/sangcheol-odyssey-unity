using System;
using SCOdyssey.Audio;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.App
{
    // 설정 DTO(SettingsData)와 오디오·타이밍 계약 값 사이의 변환. Audio와 Timing은 설정 DTO를 모르므로 여기서만 바꾼다.
    // v2 출력 필드(audioOutputType, deviceGuid, deviceName, dspBufferLength, dspBufferCount)로 부팅 요청을 만든다.
    // v1 버퍼 인덱스 변환은 설정 마이그레이션과 옛 사운드 설정 화면(S5b까지)이 쓴다.
    public static class AudioSettingsMapper
    {
        public const string Wasapi = "WASAPI";
        public const string Asio = "ASIO";
        public const int WasapiBufferCount = 4;
        public const int AsioBufferCount = 2;
        public const int DefaultBufferLength = 256;

        // 저장할 수 있는 버퍼 길이. 480은 WASAPI 10ms 주기와 맞는 값(S-0.5 SP3)이다. 확정은 SP13.
        public static readonly int[] BufferPresets = { 64, 128, 256, 480, 512, 1024 };

        private static readonly int[] V1BufferLengths = { 64, 128, 256, 512, 1024 };
        private const int DefaultV1BufferIndex = 2;

        public static AudioOutputRequest ToBootRequest(SettingsData settings)
        {
            if (settings == null) settings = new SettingsData();
            AudioOutputKind kind = ToOutputKind(settings.audioOutputType);
            Guid deviceId = ParseDeviceGuid(settings.deviceGuid);
            string deviceName = "";
            if (deviceId != Guid.Empty && settings.deviceName != null) deviceName = settings.deviceName;
            return new AudioOutputRequest(kind, deviceId, deviceName, settings.dspBufferLength, settings.dspBufferCount);
        }

        public static AudioOutputKind ToOutputKind(string outputType)
        {
            if (string.Equals(outputType, Asio, StringComparison.OrdinalIgnoreCase)) return AudioOutputKind.Asio;
            return AudioOutputKind.Wasapi;
        }

        public static string ToOutputType(AudioOutputKind kind)
        {
            if (kind == AudioOutputKind.Asio) return Asio;
            return Wasapi;
        }

        public static int DefaultBufferCount(AudioOutputKind kind)
        {
            if (kind == AudioOutputKind.Asio) return AsioBufferCount;
            return WasapiBufferCount;
        }

        // 파싱할 수 없으면 Guid.Empty(기본 장치 따라가기).
        public static Guid ParseDeviceGuid(string text)
        {
            Guid id;
            if (string.IsNullOrEmpty(text) || !Guid.TryParse(text, out id)) return Guid.Empty;
            return id;
        }

        // v1 audioBufferIndex → 길이. 범위 밖이면 256.
        public static int BufferLengthForIndex(int index)
        {
            if (index < 0 || index >= V1BufferLengths.Length) index = DefaultV1BufferIndex;
            return V1BufferLengths[index];
        }

        // 길이 → 가장 가까운 v1 인덱스(옛 사운드 설정 화면 표시용).
        public static int NearestIndexForLength(int length)
        {
            return NearestIndex(V1BufferLengths, length);
        }

        // 프리셋에 없는 길이는 가장 가까운 프리셋으로(같은 거리면 작은 쪽).
        public static int NearestPreset(int length)
        {
            return BufferPresets[NearestIndex(BufferPresets, length)];
        }

        // 판정 싱크 단계(1단계 = 3ms). JudgementDriver가 곡마다 한 번 읽는다.
        public static int ToJudgmentOffsetSteps(SettingsData settings)
        {
            if (settings == null) return 0;
            return settings.judgmentOffset;
        }

        private static int NearestIndex(int[] values, int target)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
            {
                if (Math.Abs(values[i] - target) < Math.Abs(values[best] - target)) best = i;
            }
            return best;
        }
    }
}
