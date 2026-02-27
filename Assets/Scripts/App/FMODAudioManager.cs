using System.Runtime.InteropServices;
using FMODUnity;
using UnityEngine;

namespace SCOdyssey.App
{
    // 'using FMOD;'를 사용하지 않음 - FMOD.System이 System 네임스페이스와 충돌하므로
    // 모든 FMOD 타입은 FMOD. 접두사를 붙여 완전한 경로로 참조
    public class FMODAudioManager : MonoBehaviour, IAudioManager
    {
        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private FMOD.ChannelGroup _masterGroup;
        private bool _isLoaded;
        private bool _isLoading;

        // 출력 설정 - ConfigureOutput()에서 저장
        private AudioOutputConfig _outputConfig = new AudioOutputConfig
        {
            OutputType = AudioOutputType.Default,
            DeviceIndex = 0
        };

        // -------------------------------------------------------
        // [ASIO 확장 포인트]
        // FMOD 출력 타입은 system.init() 전에 설정해야 함.
        // RuntimeManager는 Unity가 자동으로 초기화하므로,
        // ASIO 구현 시 RuntimeInitializeOnLoadMethod + RuntimeManager
        // preInit 훅 또는 수동 초기화가 필요.
        // 현재는 Default/WASAPI만 동작. ASIO 구현 시 이 주석 업데이트.
        // -------------------------------------------------------
        private void Awake()
        {
            RuntimeManager.CoreSystem.getMasterChannelGroup(out _masterGroup);
            // TODO(ASIO): _outputConfig.OutputType이 ASIO라면
            // 여기서 system.setOutput(FMOD.OUTPUTTYPE.ASIO) 적용
            // (단, RuntimeManager 수동 초기화 방식으로 전환 필요)
        }

        // NONBLOCKING 로드 완료 폴링
        private void Update()
        {
            if (!_isLoading) return;

            _sound.getOpenState(out FMOD.OPENSTATE state, out _, out _, out _);

            if (state == FMOD.OPENSTATE.READY)
            {
                _isLoaded = true;
                _isLoading = false;
                Debug.Log("[FMODAudioManager] 오디오 로드 완료.");
            }
            else if (state == FMOD.OPENSTATE.ERROR)
            {
                _isLoading = false;
                Debug.LogError("[FMODAudioManager] 오디오 로드 실패 (OPENSTATE.ERROR).");
            }
        }

        private void OnDestroy()
        {
            Stop();
            if (_sound.hasHandle()) _sound.release();
        }

        // --- IAudioManager 구현 ---

        public bool IsLoaded => _isLoaded;

        public bool IsPlaying
        {
            get
            {
                if (!_channel.hasHandle()) return false;
                _channel.isPlaying(out bool playing);
                return playing;
            }
        }

        /// <summary>
        /// FMOD CREATESTREAM + NONBLOCKING으로 오디오 파일 로드.
        /// filePath: StreamingAssets/Music/ 기준 파일명 (예: "song_0001.ogg")
        /// </summary>
        public void LoadAudio(string filePath)
        {
            // 이전 사운드 해제
            if (_sound.hasHandle())
            {
                Stop();
                _sound.release();
                _sound = default;
            }

            _isLoaded = false;
            _isLoading = false;

            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Music", filePath);

            FMOD.CREATESOUNDEXINFO exInfo = new FMOD.CREATESOUNDEXINFO();
            exInfo.cbsize = Marshal.SizeOf(exInfo);

            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
                fullPath,
                FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING,
                ref exInfo,
                out _sound);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"[FMODAudioManager] createSound 실패: {result} | 경로: {fullPath}");
                return;
            }

            _isLoading = true;
            Debug.Log($"[FMODAudioManager] 오디오 로딩 시작: {fullPath}");
        }

        /// <summary>
        /// DSP 클록 기반 sample-accurate 재생 예약.
        /// dspStartTime: GetDSPTime() + delaySeconds
        /// </summary>
        public void PlayScheduled(double dspStartTime)
        {
            if (!_isLoaded)
            {
                Debug.LogError("[FMODAudioManager] PlayScheduled: 오디오가 로드되지 않았습니다.");
                return;
            }

            RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out _, out _);

            // DSP 초 → 샘플 수 변환 (sample-accurate 스케줄링)
            ulong startDspClock = (ulong)(dspStartTime * sampleRate);

            // 일시정지 상태로 재생 시작 후 정확한 클록에 딜레이 설정
            RuntimeManager.CoreSystem.playSound(_sound, _masterGroup, true, out _channel);
            _channel.setDelay(startDspClock, 0, false);
            _channel.setPaused(false);

            Debug.Log($"[FMODAudioManager] 재생 예약 완료. DSP 클록: {startDspClock}");
        }

        public void Stop()
        {
            if (_channel.hasHandle() && IsPlaying)
                _channel.stop();
        }

        /// <summary>
        /// FMOD DSP 클록 시간을 double 초로 반환.
        /// AudioSettings.dspTime과 동등한 정밀도.
        /// getDSPClock은 FMOD.System이 아닌 ChannelGroup의 메서드.
        /// </summary>
        public double GetDSPTime()
        {
            // masterGroup의 DSP 클록 = 오디오 출력 절대 샘플 위치 (AudioSettings.dspTime 동등)
            _masterGroup.getDSPClock(out ulong clock, out _);
            RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out _, out _);
            return (double)clock / sampleRate;
        }

        public void ConfigureOutput(AudioOutputConfig config)
        {
            _outputConfig = config;
            // 현재: Default/WASAPI는 FMOD 기본값이므로 별도 처리 불필요.
            // ASIO 구현 시: pre-init 플래그 설정 후 Awake()에서 system.setOutput() 호출로 연결.
        }

        public string[] GetAvailableDevices()
        {
            // ASIO 구현 시: system.getNumDrivers() + system.getDriverInfo()로 목록 반환
            return System.Array.Empty<string>();
        }
    }
}
