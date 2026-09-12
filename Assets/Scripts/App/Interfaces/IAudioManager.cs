namespace SCOdyssey.App
{
    // 출력 드라이버 타입 - ASIO 추후 지원 예정
    public enum AudioOutputType
    {
        Default,    // OS 기본값 (Windows: WASAPI)
        WASAPI,     // Windows Audio Session API
        ASIO,       // ASIO (초저지연, 추후 구현)
    }

    // 오디오 출력 설정 - 사운드 설정 화면에서 사용 예정
    public struct AudioOutputConfig
    {
        public AudioOutputType OutputType;
        public int DeviceIndex;   // ASIO 드라이버 선택용 (기본 0)
    }

    // 원샷 사운드가 라우팅될 버스. 볼륨은 각각 SetHitSoundVolume / SetSfxVolume이 제어
    public enum AudioBus
    {
        HitSound = 0,
        Sfx      = 1,
    }

    public interface IAudioManager
    {
        void LoadAudio(string filePath, bool loopHint=false);         // StreamingAssets/Music/ 기준 파일명
        void PlayScheduled(double dspStartTime, bool loopPlay=false); // sample-accurate 재생 예약
        void Stop();
        void Pause();
        void Resume();
        double GetDSPTime();   // AudioSettings.dspTime 대체 (double 정밀도 필수)
        bool IsPlaying { get; }
        bool IsLoaded { get; } // GameDataLoader의 로딩 대기용

        // 출력 장치 설정. FMOD 초기화 전에 호출해야 함.
        // 현재는 Default(WASAPI)만 동작. ASIO는 추후 구현.
        void ConfigureOutput(AudioOutputConfig config);

        // 사용 가능한 오디오 드라이버 목록 조회 (설정 UI용)
        string[] GetAvailableDevices();

        // 재생 장치 변경 (FMOD setDriver — 런타임 호출 가능)
        void SetAudioDevice(int driverIndex);

        // 원샷(타격음/효과음) 재생. BGM과 달리 짧은 사운드를 겹쳐서 여러 번 울린다.
        //
        // StreamingAssets/HitSound/ 의 파일을 메모리에 디코드해두고 슬롯 인덱스를 반환.
        // 같은 파일명을 다시 등록하면 기존 슬롯을 그대로 돌려준다(멱등) — 씬 재진입/리트라이에 안전.
        // 로드 시점 전용이며 실패 시 -1을 반환한다.
        int RegisterOneShot(string fileName);

        // 등록된 슬롯을 즉시 재생. 입력 판정 경로에서 불리는 핫패스이므로 할당·문자열 조회가 없다.
        // 슬롯이 유효하지 않으면(-1 등) 조용히 무시하므로 음원이 없어도 게임은 그대로 돌아간다.
        void PlayOneShot(int slot, AudioBus bus = AudioBus.HitSound, float volume = 1f);

        // 볼륨 제어 (0 ~ 1) — SettingsManager.Apply()에서 호출
        void SetMasterVolume(float volume);
        void SetBgmVolume(float volume);
        void SetHitSoundVolume(float volume);
        void SetSfxVolume(float volume);
    }
}
