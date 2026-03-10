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
        void LoadAudio(string filePath);         // StreamingAssets/Music/ 기준 파일명 (LocalContentProvider에서 사용)
        void LoadAudioFromBytes(byte[] audioData); // 복호화된 바이트 배열에서 FMOD OPENMEMORY 방식으로 로드
        void PlayScheduled(double dspStartTime); // sample-accurate 재생 예약
        void Stop();
        double GetDSPTime();   // AudioSettings.dspTime 대체 (double 정밀도 필수)
        bool IsPlaying { get; }
        bool IsLoaded { get; } // GameDataLoader의 로딩 대기용

        // 출력 장치 설정. FMOD 초기화 전에 호출해야 함.
        // 현재는 Default(WASAPI)만 동작. ASIO는 추후 구현.
        void ConfigureOutput(AudioOutputConfig config);

        // 사용 가능한 오디오 드라이버 목록 조회 (설정 UI용)
        // 현재는 빈 배열 반환. ASIO 구현 시 채움.
        string[] GetAvailableDevices();
    }
}
