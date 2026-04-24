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

        // 볼륨 제어 (0 ~ 1) — SettingsManager.Apply()에서 호출
        void SetMasterVolume(float volume);
        void SetBgmVolume(float volume);
        void SetHitSoundVolume(float volume);
        void SetSfxVolume(float volume);
    }
}
