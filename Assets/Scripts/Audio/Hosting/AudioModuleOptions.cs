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

        // 로비에서 포커스를 잃었을 때 계속 재생할지. 포커스가 바뀔 때마다 읽는다. null이면 계속 재생한다.
        public Func<bool> PlayInBackground { get; set; }

        // RuntimeManager가 초기화되어 있으면 오류 로그를 남긴다. 옛 오디오 경로가 남아 있는 동안(S4b 전)은 게임에서 끈다.
        public bool EnforceRuntimeManagerGuard { get; set; }
    }
}
