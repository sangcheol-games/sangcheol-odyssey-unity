using FMODUnity;
using SCOdyssey.Core;
using SCOdyssey.Domain.Service;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SCOdyssey.App
{
    // 'using FMOD;'를 사용하지 않음 - FMOD.System이 System 네임스페이스와 충돌하므로
    // 모든 FMOD 타입은 FMOD. 접두사를 붙여 완전한 경로로 참조
    public class FMODAudioManager : MonoBehaviour, IAudioManager
    {
        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private FMOD.ChannelGroup _masterGroup;    // FMOD 시스템 마스터 (getDSPClock 전용)
        private FMOD.ChannelGroup _ourMasterGroup; // 게임 전체 볼륨 제어
        private FMOD.ChannelGroup _bgmGroup;       // 배경음(음악) 볼륨
        private FMOD.ChannelGroup _hitSoundGroup;  // 타격음 볼륨
        private FMOD.ChannelGroup _sfxGroup;       // 효과음 볼륨
        private bool _isLoaded;
        private bool _isLoading;
        private bool _LoadFailed;       // 현재 플래그 사용은 미구현

        // 출력 설정 - ConfigureOutput()에서 저장
        private AudioOutputConfig _outputConfig = new AudioOutputConfig
        {
            OutputType = AudioOutputType.Default,
            DeviceIndex = 0
        };


        // 이걸 걍 돌려써도 좋을거같다
        private struct ManageSFX
        {
            public FMOD.Sound sound;
            public FMOD.Channel channel;
            public bool isLoaded;
            public bool play;
            // lastPlayedTime 같은거 기록해야할수도
        }

        private struct HitSounds
        {
            public int cLoaded;
            public ManageSFX Perfect;
            public ManageSFX Master;
            public ManageSFX Ideal;
            public ManageSFX Kind;
            public ManageSFX Umm;
        } HitSounds _hitSound;

        private const int HIT_SOUND_SIZE = 5;

        // Path: StreamingAssets/SFX/
        private static readonly string SFX_Perfect_Filename = "SFX_Perfect.wav";
        private static readonly string SFX_Master_Filename = "SFX_Master.wav";
        private static readonly string SFX_Ideal_Filename = "SFX_Ideal.wav";
        private static readonly string SFX_Kind_Filename = "SFX_Kind.wav";
        private static readonly string SFX_Umm_Filename = "SFX_Umm.wav";




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
            // DSP 클록 조회용 시스템 마스터
            RuntimeManager.CoreSystem.getMasterChannelGroup(out _masterGroup);

            // 게임 볼륨 제어용 ChannelGroup 계층 생성
            RuntimeManager.CoreSystem.createChannelGroup("Master",   out _ourMasterGroup);
            RuntimeManager.CoreSystem.createChannelGroup("BGM",      out _bgmGroup);
            RuntimeManager.CoreSystem.createChannelGroup("HitSound", out _hitSoundGroup);
            RuntimeManager.CoreSystem.createChannelGroup("SFX",      out _sfxGroup);
            _ourMasterGroup.addGroup(_bgmGroup,      false, out _);
            _ourMasterGroup.addGroup(_hitSoundGroup, false, out _);
            _ourMasterGroup.addGroup(_sfxGroup,      false, out _);

            // TODO(ASIO): _outputConfig.OutputType이 ASIO라면
            // 여기서 system.setOutput(FMOD.OUTPUTTYPE.ASIO) 적용
            // (단, RuntimeManager 수동 초기화 방식으로 전환 필요)


            // HitSound 파일 로드
            // 게임 실행 시 바로 로드, 온메모리 사용
            #region LoadHitSounds

            _LoadFailed = false;
            _hitSound.cLoaded = 0;

            _hitSound.Perfect.isLoaded = false;
            LoadSFX(ref _hitSound.Perfect.sound, SFX_Perfect_Filename);
            _hitSound.Perfect.play = false;

            _hitSound.Master.isLoaded = false;
            LoadSFX(ref _hitSound.Master.sound, SFX_Master_Filename);
            _hitSound.Master.play = false;

            _hitSound.Ideal.isLoaded = false;
            LoadSFX(ref _hitSound.Ideal.sound, SFX_Ideal_Filename);
            _hitSound.Ideal.play = false;

            _hitSound.Kind.isLoaded = false;
            LoadSFX(ref _hitSound.Kind.sound, SFX_Kind_Filename);
            _hitSound.Kind.play = false;

            _hitSound.Umm.isLoaded = false;
            LoadSFX(ref _hitSound.Umm.sound, SFX_Umm_Filename);
            _hitSound.Umm.play = false;

            #endregion
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 포커스를 잃고 백그라운드 재생이 꺼져 있으면 음소거
            bool shouldMute = !hasFocus && !ServiceLocator.Get<ISettingsManager>().Current.playInBackground;
            _ourMasterGroup.setMute(shouldMute);
        }

        // NONBLOCKING 로드 완료 폴링
        private void Update()
        {
            // SFX 로드체크
            // TODO: 좀 읽기 쉽게 만들기..

            if (!_LoadFailed && _hitSound.cLoaded < HIT_SOUND_SIZE)
            {
                if (!_hitSound.Perfect.isLoaded){
                    if (CheckSoundLoaded(ref _hitSound.Perfect.sound))
                    {
                        _hitSound.Perfect.isLoaded = true;
                        _hitSound.cLoaded++;
                    }
                }

                if (!_hitSound.Master.isLoaded){
                    if (CheckSoundLoaded(ref _hitSound.Master.sound))
                    {
                        _hitSound.Master.isLoaded = true;
                        _hitSound.cLoaded++;
                    }
                }

                if (!_hitSound.Ideal.isLoaded){
                    if (CheckSoundLoaded(ref _hitSound.Ideal.sound))
                    {
                        _hitSound.Ideal.isLoaded = true;
                        _hitSound.cLoaded++;
                    }
                }

                if (!_hitSound.Kind.isLoaded){
                    if (CheckSoundLoaded(ref _hitSound.Kind.sound))
                    {
                        _hitSound.Kind.isLoaded = true;
                        _hitSound.cLoaded++;
                    }
                }

                if (!_hitSound.Umm.isLoaded){
                    if (CheckSoundLoaded(ref _hitSound.Umm.sound))
                    {
                        _hitSound.Umm.isLoaded = true;
                        _hitSound.cLoaded++;
                    }
                }
            }
            
            
            else if (!_isLoading) return;

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

        void LateUpdate()
        {
            if (_LoadFailed || _hitSound.cLoaded < HIT_SOUND_SIZE)
                return;

            if (_hitSound.Perfect.play)
            {
                PlaySFX(ref _hitSound.Perfect);
                _hitSound.Perfect.play = false;
            }

            if (_hitSound.Master.play)
            {
                PlaySFX(ref _hitSound.Master);
                _hitSound.Master.play = false;
            }

            if (_hitSound.Ideal.play)
            {
                PlaySFX(ref _hitSound.Ideal);
                _hitSound.Ideal.play = false;
            }

            if (_hitSound.Kind.play)
            {
                PlaySFX(ref _hitSound.Kind);
                _hitSound.Kind.play = false;
            }

            if (_hitSound.Umm.play)
            {
                PlaySFX(ref _hitSound.Umm);
                _hitSound.Umm.play = false;
            }
        }

        private void OnDestroy()
        {
            Stop();

            if (_sound.hasHandle()) _sound.release();
            if (_hitSound.Perfect.sound.hasHandle()) _hitSound.Perfect.sound.release();
            if (_hitSound.Master.sound.hasHandle()) _hitSound.Master.sound.release();
            if (_hitSound.Ideal.sound.hasHandle()) _hitSound.Ideal.sound.release();
            if (_hitSound.Kind.sound.hasHandle()) _hitSound.Kind.sound.release();
            if (_hitSound.Umm.sound.hasHandle()) _hitSound.Umm.sound.release();

            _bgmGroup.release();
            _hitSoundGroup.release();
            _sfxGroup.release();
            _ourMasterGroup.release();
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
        /// loopHint: Audio Asset의 기본 성격이 단일 재생인지 루프 재생인지.
        /// </summary>
        public void LoadAudio(string filePath, bool loopHint)
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

            var loopFlag = loopHint ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.DEFAULT;
            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
                fullPath,
                FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING | loopFlag,
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
        /// loopPlay: 단일 재생인지 루프 재생인지
        /// </summary>
        public void PlayScheduled(double dspStartTime, bool loopPlay)
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
            RuntimeManager.CoreSystem.playSound(_sound, _bgmGroup, true, out _channel);
            _channel.setDelay(startDspClock, 0, false);
            // -1 for inf, 0 for 1 loop, N for N+1 loop
            _channel.setLoopCount(loopPlay ? -1 : 0);
            _channel.setPaused(false);

            Debug.Log($"[FMODAudioManager] 재생 예약 완료. DSP 클록: {startDspClock}");
        }

        public void PlayHitSound(Constants.JudgeType type)
        {
            if (_hitSound.cLoaded < HIT_SOUND_SIZE)
            {
                Debug.LogError("[FMODAudioManager] PlayHitSound: 오디오가 로드되지 않았습니다.");
                return;
            }

            // 실행 예약을 걸어놓는다 (LateUpdate에서 한번만 실행)
            switch (type)
            {
                case Constants.JudgeType.Perfect:
                    _hitSound.Perfect.play = true;
                    break;
                case Constants.JudgeType.Master:
                    _hitSound.Master.play = true;
                    break;
                case Constants.JudgeType.Ideal:
                    _hitSound.Ideal.play = true;
                    break;
                case Constants.JudgeType.Kind:
                    _hitSound.Kind.play = true;
                    break;
                case Constants.JudgeType.Umm:
                    _hitSound.Umm.play = true;
                    break;
            }
        }

        public void Stop()
        {
            if (_channel.hasHandle() && IsPlaying)
                _channel.stop();
        }

        public void Pause()  { if (_channel.hasHandle()) _channel.setPaused(true); }
        public void Resume() { if (_channel.hasHandle()) _channel.setPaused(false); }

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
            RuntimeManager.CoreSystem.getNumDrivers(out int count);
            var names = new string[count];
            for (int i = 0; i < count; i++)
                RuntimeManager.CoreSystem.getDriverInfo(i, out names[i], 256, out _, out _, out _, out _);
            return names;
        }

        public void SetAudioDevice(int driverIndex)
        {
            RuntimeManager.CoreSystem.setDriver(driverIndex);
        }

        public void SetMasterVolume(float volume)   => _ourMasterGroup.setVolume(volume);
        public void SetBgmVolume(float volume)       => _bgmGroup.setVolume(volume);
        public void SetHitSoundVolume(float volume)  => _hitSoundGroup.setVolume(volume);
        public void SetSfxVolume(float volume)       => _sfxGroup.setVolume(volume);

        // 현재 Awake에서 즉시 로드
        private void LoadSFX(ref FMOD.Sound sound, string fileName)
        {
            if (sound.hasHandle())
                return;

            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "SFX", fileName);

            FMOD.CREATESOUNDEXINFO exInfo = new FMOD.CREATESOUNDEXINFO();
            exInfo.cbsize = Marshal.SizeOf(exInfo);

            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(
                fullPath,
                FMOD.MODE.CREATESAMPLE | FMOD.MODE.NONBLOCKING,
                ref exInfo,
                out sound);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"[FMODAudioManager] createSound 실패: {result} | 경로: {fullPath}");
                _LoadFailed = true;
                return;
            }

            Debug.Log($"[FMODAudioManager] 오디오 로딩 시작: {fullPath}");
        }

        private bool CheckSoundLoaded(ref FMOD.Sound sound)
        {
            sound.getOpenState(out FMOD.OPENSTATE state, out _, out _, out _);

            if (state == FMOD.OPENSTATE.READY)
            {
                return true;
            }
            else if (state == FMOD.OPENSTATE.ERROR)
            {
                Debug.LogError($"[FMODAudioManager] 오디오 로드 실패 (OPENSTATE.ERROR).");
                _LoadFailed = true;
                // TODO: 타이틀로 보내버리거나 청소하고 다시시도
            }
            return false;
        }

        private void PlaySFX(ref ManageSFX sfx)
        {
            RuntimeManager.CoreSystem.playSound(sfx.sound, _hitSoundGroup, true, out sfx.channel);
        }
    }
}
