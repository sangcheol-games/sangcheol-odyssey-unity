using System.Collections.Generic;
using System.Runtime.InteropServices;
using FMODUnity;
using SCOdyssey.Core;
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

        // 출력 샘플레이트. Awake에서 1회만 조회해 캐싱한다.
        // GetDSPTime()은 매 프레임 호출되므로 여기서 getSoftwareFormat을 반복 호출하면 낭비다.
        private int _sampleRate;

        // RuntimeManager.CoreSystem은 Instance getter 체인을 타므로 원샷 핫패스용으로 캐싱
        private FMOD.System _coreSystem;

        // AudioBus 인덱스와 1:1로 대응하는 출력 그룹. Awake에서 구성
        private FMOD.ChannelGroup[] _busGroups;

        // 원샷 사운드 슬롯. 로드 시점에만 늘어나고 게임 중에는 인덱싱만 한다.
        // _oneShotSlots는 파일명 → 슬롯 매핑으로, RegisterOneShot을 멱등으로 만들어
        // 씬 재진입·리트라이 시 중복 로드와 핸들 누수를 막는다.
        private const int MAX_ONESHOT_SLOTS = 32;
        private readonly FMOD.Sound[] _oneShots = new FMOD.Sound[MAX_ONESHOT_SLOTS];
        private readonly Dictionary<string, int> _oneShotSlots = new Dictionary<string, int>();
        private int _oneShotCount;

        // 원샷 로드 모드. CREATESAMPLE이 핵심 — 아래 RegisterOneShot 주석 참고
        private const FMOD.MODE ONESHOT_MODE =
              FMOD.MODE.CREATESAMPLE
            | FMOD.MODE._2D
            | FMOD.MODE.LOOP_OFF
            | FMOD.MODE.IGNORETAGS
            | FMOD.MODE.LOWMEM;

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
            // 코어 시스템 캐싱 (원샷 핫패스에서 Instance getter 체인을 타지 않도록)
            _coreSystem = RuntimeManager.CoreSystem;

            // DSP 클록 조회용 시스템 마스터
            _coreSystem.getMasterChannelGroup(out _masterGroup);

            // 샘플레이트 캐싱 (GetDSPTime에서 매 프레임 재조회하지 않도록)
            _coreSystem.getSoftwareFormat(out _sampleRate, out _, out _);

            LogAudioLatency();

            // 게임 볼륨 제어용 ChannelGroup 계층 생성
            RuntimeManager.CoreSystem.createChannelGroup("Master",   out _ourMasterGroup);
            RuntimeManager.CoreSystem.createChannelGroup("BGM",      out _bgmGroup);
            RuntimeManager.CoreSystem.createChannelGroup("HitSound", out _hitSoundGroup);
            RuntimeManager.CoreSystem.createChannelGroup("SFX",      out _sfxGroup);
            _ourMasterGroup.addGroup(_bgmGroup,      false, out _);
            _ourMasterGroup.addGroup(_hitSoundGroup, false, out _);
            _ourMasterGroup.addGroup(_sfxGroup,      false, out _);

            // AudioBus enum 값이 곧 이 배열의 인덱스 — 순서를 바꾸면 안 된다
            _busGroups = new[] { _hitSoundGroup, _sfxGroup };

            // TODO(ASIO): _outputConfig.OutputType이 ASIO라면
            // 여기서 system.setOutput(FMOD.OUTPUTTYPE.ASIO) 적용
            // (단, RuntimeManager 수동 초기화 방식으로 전환 필요)
        }

        /// <summary>
        /// 실제로 적용된 DSP 버퍼와 그로 인한 출력 지연을 시작 시 1회 기록한다.
        /// 설정값(audioBufferIndex)이 FMOD에 반영됐는지 확인하는 유일한 수단이므로 상시 유지한다.
        /// 평균 지연 공식은 FMOD System::setDSPBufferSize 문서 기준:
        ///   블록(ms) = length * 1000 / sampleRate,  평균 지연 = 블록 * (count - 1.5)
        /// </summary>
        private void LogAudioLatency()
        {
            RuntimeManager.CoreSystem.getDSPBufferSize(out uint length, out int count);
            float blockMs = length * 1000f / _sampleRate;
            Debug.Log($"[FMODAudioManager] DSP 버퍼 {length} x {count} @{_sampleRate}Hz | " +
                      $"블록 {blockMs:F2}ms, 평균 출력 지연 {blockMs * (count - 1.5f):F2}ms");
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

            // ChannelGroup보다 먼저 해제 — _busGroups가 아래 그룹들을 참조한다
            for (int i = 0; i < _oneShotCount; i++)
                if (_oneShots[i].hasHandle()) _oneShots[i].release();
            _oneShotCount = 0;
            _oneShotSlots.Clear();

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

            // DSP 초 → 샘플 수 변환 (sample-accurate 스케줄링)
            ulong startDspClock = (ulong)(dspStartTime * _sampleRate);

            // 일시정지 상태로 재생 시작 후 정확한 클록에 딜레이 설정
            _coreSystem.playSound(_sound, _bgmGroup, true, out _channel);
            _channel.setDelay(startDspClock, 0, false);
            // -1 for inf, 0 for 1 loop, N for N+1 loop
            _channel.setLoopCount(loopPlay ? -1 : 0);
            // 0 = 최고 우선순위. 타격음이 몰려도 BGM이 보이스 스틸링 대상이 되면 안 된다
            _channel.setPriority(0);
            _channel.setPaused(false);

            Debug.Log($"[FMODAudioManager] 재생 예약 완료. DSP 클록: {startDspClock}");
        }

        /// <summary>
        /// 원샷 사운드를 슬롯에 등록하고 인덱스를 반환. 로드 시점 전용(핫패스 아님).
        /// fileName: StreamingAssets/HitSound/ 기준 파일명. 실패 시 -1.
        /// 같은 파일명을 다시 등록하면 기존 슬롯을 그대로 돌려준다.
        /// </summary>
        public int RegisterOneShot(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return -1;

            // 이미 로드된 파일이면 같은 슬롯 재사용 — 씬 재진입/리트라이 시 중복 로드·핸들 누수 방지
            if (_oneShotSlots.TryGetValue(fileName, out int cached)) return cached;

            if (_oneShotCount >= MAX_ONESHOT_SLOTS)
            {
                Debug.LogError($"[FMODAudioManager] 원샷 슬롯이 가득 찼습니다({MAX_ONESHOT_SLOTS}): {fileName}");
                return -1;
            }

            // 주의: StreamingAssets는 Android에서 jar 내부 경로가 되어 직접 읽을 수 없다.
            //       현재는 데스크톱 전용 (LoadAudio도 동일한 제약).
            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "HitSound", fileName);

            FMOD.CREATESOUNDEXINFO exInfo = new FMOD.CREATESOUNDEXINFO();
            exInfo.cbsize = Marshal.SizeOf(exInfo);

            // CREATESAMPLE: 전체를 PCM으로 디코드해 메모리 상주 → 재생 시점 디스크 I/O·디코드 0.
            // CREATESTREAM을 쓰면 안 되는 이유는 두 가지다.
            //  1) 재생할 때마다 디스크 읽기 + 디코드가 발생해 지연이 튄다.
            //  2) 스트림 Sound 하나는 동시에 한 채널로만 재생된다 → 4레인 연타 시 이전 타격음이 끊긴다.
            // NONBLOCKING을 쓰지 않는 이유: 파일이 작아 동기 로드가 1ms 남짓이고,
            // 폴링 상태 기계를 두면 "첫 노트인데 아직 안 올라왔다" 레이스가 생긴다.
            FMOD.RESULT result = _coreSystem.createSound(fullPath, ONESHOT_MODE, ref exInfo, out FMOD.Sound sound);

            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError($"[FMODAudioManager] 원샷 로드 실패: {result} | 경로: {fullPath}");
                return -1;
            }

            _oneShots[_oneShotCount] = sound;
            _oneShotSlots[fileName] = _oneShotCount;
            return _oneShotCount++;
        }

        /// <summary>
        /// 등록된 원샷을 즉시 재생. 입력 판정 경로에서 불리는 핫패스라 할당·문자열 조회·로그가 없다.
        /// </summary>
        public void PlayOneShot(int slot, AudioBus bus, float volume)
        {
            // 음수(로드 실패 -1)와 범위 초과를 부호없는 비교 한 번으로 걸러낸다
            if ((uint)slot >= (uint)_oneShotCount) return;

            // paused:false로 바로 시작 — setDelay를 쓰지 않으므로 pause→unpause 왕복이 불필요하다.
            // 믹서가 다음 블록 경계에서 픽업하는 것이 달성 가능한 최소 지연이다.
            // (입력 시각으로 setDelay를 걸어봐야 그 시각은 이미 믹스 커서가 지나간 과거라 의미가 없다)
            _coreSystem.playSound(_oneShots[slot], _busGroups[(int)bus], false, out FMOD.Channel channel);

            if (volume != 1f) channel.setVolume(volume);   // 기본 볼륨이면 P/Invoke 한 번을 절약
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
            return (double)clock / _sampleRate;   // sampleRate는 Awake에서 캐싱 (매 프레임 호출되는 경로)
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
    }
}
