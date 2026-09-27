namespace SCOdyssey.App
{
    // 오디오 관련 새 UI 문자열을 한 곳에 모은다(나중에 Localization 테이블로 옮기기 쉽게).
    public static class AudioUiText
    {
        public const string DefaultDevice = "기본 장치";
        public const string Searching = "검색 중…";
        public const string DeviceMissing = " (연결 안 됨)";
        public const string BootFallbackNotice = "[Audio] 저장한 출력 구성으로 열지 못해 대체 구성으로 시작했습니다. 사운드 설정에서 출력 장치를 확인하세요.";
        public const string BootUnavailableNotice = "[Audio] 오디오 장치를 열지 못해 소리 없이 시작했습니다. 사운드 설정에서 출력 장치를 확인하세요.";
    }
}
