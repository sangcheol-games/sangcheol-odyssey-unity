using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class SettingsData
    {
        // Game
        public float noteSpeed = 2.0f;      // 0.5 ~ 5.0
        public int audioOffsetMs = 0;       // 노트싱크 오프셋 -200 ~ 200 ms
        public int judgmentOffset = 0;      // 판정 타이밍 오프셋 -20 ~ 20 (1단위 = 3ms)
        public bool autoPlay = false;
        public string languageCode = "ko-KR";       // BCP 47 (ko-KR / ja-JP / en-US)
        public string displayLanguageCode = "origin";  // 곡 제목 표시 언어 (origin / ko-KR / ja-JP / en-US)
        public float bgaOpacity = 0.4f;               // BGA 투명도 0 ~ 1
        public float noteOpacity = 0.2f;              // 고스트 노트 투명도 0 ~ 0.5

        // Graphic
        public bool fullScreen = true;
        public int targetFrameRate = 60;    // 30 / 60 / 120 / -1(무제한)
        public int resolutionIndex = 0;     // Screen.resolutions 배열 인덱스

        // Sound
        public float masterVolume = 1f;     // 0 ~ 1
        public float bgmVolume = 1f;
        public float sfxVolume = 1f;
        public int audioDeviceIndex = 0;    // FMOD 출력 장치 인덱스
    }
}
