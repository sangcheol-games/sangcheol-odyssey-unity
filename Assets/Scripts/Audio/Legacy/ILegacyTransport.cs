namespace SCOdyssey.Audio.Legacy
{
    // [과도기] 옛 IAudioManager 의미(단일 음악 슬롯, DSP 초 단위 예약, int 원샷 슬롯)를 새 엔진 위에서 재현한다.
    // App의 LegacyAudioManagerAdapter만 쓰며, 모든 소비자가 새 계약으로 옮겨 가면(C 단계) 삭제한다.
    public interface ILegacyTransport
    {
        // 음악 폴더 기준 파일명. 이전 음원은 해제한다. 엔진을 쓸 수 없으면 곧바로 IsLoaded가 된다.
        void Load(string fileName, bool loop);
        bool IsLoaded { get; }
        bool IsPlaying { get; }

        // DspSeconds 기준 시각에 재생을 예약한다. 이미 지난 시각이면 곧바로 재생한다.
        void PlayAt(double dspSeconds, bool loop);
        void Pause();
        void Resume();
        void Stop();

        // 옛 GetDSPTime. 재구성과 엔진 상실을 넘어도 단조 증가한다(엔진을 쓸 수 없으면 QPC로 진행).
        double DspSeconds { get; }

        int RegisterOneShot(string fileName);   // 실패하면 -1. 파일명으로 멱등이고 재구성 뒤에도 같은 번호가 유효하다
        void PlayOneShot(int slot);             // 잘못된 번호는 무시한다

        string[] GetDeviceNames();              // 현재 출력 타입의 장치 이름(목록이 비었으면 곧바로 열거)
        void SelectDevice(int index);           // 목록 번호의 장치로 설정 적용(close→init). 범위 밖이면 무시
    }
}
