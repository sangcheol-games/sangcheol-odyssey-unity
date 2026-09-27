using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class SettingsData
    {
        // Game
        public int audioOffsetMs = 0;       // 노트 출력 타이밍 오프셋 -200 ~ 200 ms
        public int judgmentOffset = 0;      // 판정 타이밍 오프셋 -20 ~ 20 (1단위 = 3ms)
        public string languageCode = "ko-KR";       // BCP 47 (ko-KR / ja-JP / en-US)
        public string displayLanguageCode = "origin";  // 곡 제목 표시 언어 (origin / ko-KR / ja-JP / en-US)
        public float bgaOpacity = 0.4f;               // BGA 투명도 0 ~ 1
        public float noteOpacity = 0.2f;              // 고스트 노트 투명도 0 ~ 0.5

        // Graphic
        public int displayMode = 0;         // 0=전체 화면 / 1=창 모드 / 2=전체 창 모드
        public int targetFrameRate = 60;    // 30 / 60 / 120 / -1(무제한)
        public int resolutionIndex = 4;     // 0~4: 1024×768, 1280×720, 1366×768, 1600×900, 1920×1080

        // Sound
        public float masterVolume = 1f;       // 0 ~ 1
        public float bgmVolume = 1f;          // 배경음(음악)
        public float hitSoundVolume = 1f;     // 타격음
        public float sfxVolume = 1f;          // 효과음
        [Obsolete("v1 필드. 출력 장치는 deviceGuid/deviceName을 쓴다. S5b에서 새 사운드 설정 화면으로 바꾸면 쓰는 곳이 없어진다.")]
        public int audioDeviceIndex = 0;     // FMOD 출력 장치 인덱스
        [Obsolete("v1 필드. 버퍼는 dspBufferLength를 쓴다. S5b까지 옛 사운드 설정 화면이 표시용으로만 쓴다.")]
        public int audioBufferIndex = 2;      // 0=64 / 1=128 / 2=256 / 3=512 / 4=1024
        public bool playInBackground = false; // true=백그라운드 재생 / false=포커스 잃으면 음소거

        // Sound v2 (출력). 부팅 때 AudioSettingsMapper가 읽고, 설정 화면은 적용이 요청 구성으로 성공했을 때만 저장한다.
        public const int CurrentVersion = 2;
        public int settingsVersion = CurrentVersion;
        public string audioOutputType = "WASAPI";   // "WASAPI" | "ASIO"
        public string deviceGuid = "";              // 비어 있으면 기본 장치 따라가기
        public string deviceName = "";              // GUID로 못 찾을 때의 보조 키, 표시용
        public int systemRate = 0;                  // 마지막으로 확인한 장치 레이트
        public int dspBufferLength = 256;
        public int dspBufferCount = 4;              // UI에 노출하지 않음. ASIO는 2

        // Input
        public int inputPollingRateHz = 2000;  // 입력 폴링레이트 (Hz): 1000 / 2000 / 4000 / 8000
    }
}
