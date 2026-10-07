using System;

namespace SCOdyssey.Audio.Hosting
{
    // 설치 인자. Audio는 설정 DTO를 모르므로 App(AudioSettingsMapper)이 값을 채워 넘긴다.
    public sealed class AudioModuleOptions
    {
        public AudioOutputRequest Output { get; set; } = new AudioOutputRequest(AudioOutputKind.Wasapi, Guid.Empty, "", 256, 4);

        // 요청 구성을 건너뛰고 WASAPI 기본 512×4부터 시도한다. 명령행 -sco-audio-safe도 같다.
        public bool SafeMode { get; set; }

        // 타격음 파일 폴더(전체 경로).
        public string HitSoundFolder { get; set; } = "";

        // 곡·로비 BGM·프리뷰 음원 폴더(전체 경로).
        public string MusicFolder { get; set; } = "";

        // 로비에서 포커스를 잃었을 때 계속 재생할지. 포커스가 바뀔 때마다 읽는다. null이면 계속 재생한다.
        public Func<bool> PlayInBackground { get; set; }

        // 게임 곡이 흐르는 중 포커스를 잃으면 일시정지한다(게임은 항상 켠다. 하네스에서만 끌 수 있다).
        public bool PauseSongOnFocusLoss { get; set; } = true;

        // 부팅과 씬 로드 때 RuntimeManager가 초기화되어 있으면 오류 로그를 남긴다(게임과 하네스는 켠다).
        public bool EnforceRuntimeManagerGuard { get; set; }
    }
}
