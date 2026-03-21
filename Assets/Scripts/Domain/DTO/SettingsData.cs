using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class SettingsData
    {
        // Game
        public float noteSpeed = 2.0f;      // 0.5 ~ 5.0
        public int audioOffsetMs = 0;       // 노트 출력 타이밍 오프셋 -200 ~ 200 ms
        public int judgmentOffset = 0;      // 판정 타이밍 오프셋 -20 ~ 20 (1단위 = 3ms)
        public bool autoPlay = false;
        public string languageCode = "ko-KR";       // BCP 47 (ko-KR / ja-JP / en-US)
        public string displayLanguageCode = "origin";  // 곡 제목 표시 언어 (origin / ko-KR / ja-JP / en-US)
        public float bgaOpacity = 0.4f;               // BGA 투명도 0 ~ 1
        public float noteOpacity = 0.2f;              // 고스트 노트 투명도 0 ~ 0.5

        // Graphic
        public int displayMode = 0;         // 0=전체 화면 / 1=창 모드 / 2=전체 창 모드
        public int targetFrameRate = 60;    // 30 / 60 / 120 / -1(무제한)
        public int resolutionIndex = 5;     // 0~5: 1024×576, 1152×648, 1280×720, 1366×768, 1600×900, 1920×1080

        // Sound
        public float masterVolume = 1f;       // 0 ~ 1
        public float bgmVolume = 1f;          // 배경음(음악)
        public float hitSoundVolume = 1f;     // 타격음
        public float sfxVolume = 1f;          // 효과음
        public int audioDeviceIndex = 0;     // FMOD 출력 장치 인덱스
        public int audioBufferIndex = 2;     // 0=64 / 1=128 / 2=256 / 3=512 / 4=1024
    }
}
