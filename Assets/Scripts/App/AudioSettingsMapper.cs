using System;
using SCOdyssey.Audio;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.App
{
    // 설정 DTO(SettingsData)와 오디오·타이밍 계약 값 사이의 변환. Audio와 Timing은 설정 DTO를 모르므로 여기서만 바꾼다.
    // v1(현재 설정): 출력은 WASAPI, 장치는 기본 장치 따라가기(옛 코드도 부팅 때 장치를 복원하지 않았다),
    // 버퍼 길이는 audioBufferIndex → {64, 128, 256, 512, 1024}, 개수 4(옛 FMODAudioPreInit과 같음). v2는 S5a에서 바꾼다.
    public static class AudioSettingsMapper
    {
        private static readonly int[] V1BufferLengths = { 64, 128, 256, 512, 1024 };
        private const int DefaultBufferIndex = 2;
        public const int WasapiBufferCount = 4;

        public static AudioOutputRequest ToBootRequest(SettingsData settings)
        {
            int index = DefaultBufferIndex;
            if (settings != null) index = settings.audioBufferIndex;
            return new AudioOutputRequest(AudioOutputKind.Wasapi, Guid.Empty, "", BufferLengthForIndex(index), WasapiBufferCount);
        }

        public static int BufferLengthForIndex(int index)
        {
            if (index < 0 || index >= V1BufferLengths.Length) index = DefaultBufferIndex;
            return V1BufferLengths[index];
        }

        // 판정 싱크 단계(1단계 = 3ms). JudgementDriver가 곡마다 한 번 읽는다.
        public static int ToJudgmentOffsetSteps(SettingsData settings)
        {
            if (settings == null) return 0;
            return settings.judgmentOffset;
        }
    }
}
