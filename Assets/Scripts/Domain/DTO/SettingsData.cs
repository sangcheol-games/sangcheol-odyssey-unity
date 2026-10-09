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
        public float bgaOpacity = 0.15f;              // BGA 투명도 0 ~ 1
        public float noteOpacity = 0.15f;             // 고스트(대기) 노트 투명도(알파) 0 ~ 0.5 (UI 표시값 = 저장값 / 0.5)

        // Graphic
        public int displayMode = 0;         // 0=전체 화면 / 1=창 모드 / 2=전체 창 모드
        public int targetFrameRate = 60;    // 30 / 60 / 120 / -1(무제한)
        public int resolutionIndex = 4;     // 0~4: 1024×768, 1280×720, 1366×768, 1600×900, 1920×1080

        // Sound
        public float masterVolume = 1f;       // 0 ~ 1
        public float bgmVolume = 1f;          // 배경음(음악)
        public float hitSoundVolume = 1f;     // 타격음
        public float sfxVolume = 1f;          // 효과음
        public bool playInBackground = false; // true=백그라운드 재생 / false=포커스 잃으면 음소거

        // Sound v2 (출력). 부팅 때 AudioSettingsMapper가 읽고, 설정 화면은 적용이 요청 구성으로 성공했을 때만 저장한다.
        // v1의 audioDeviceIndex·audioBufferIndex는 지웠다(마이그레이션은 SettingsMigration이 원문 JSON에서 읽는다).
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
