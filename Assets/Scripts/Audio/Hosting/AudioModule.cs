using System;
using System.Collections.Generic;
using System.Threading;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Diagnostics;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Legacy;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Output;
using SCOdyssey.Audio.Playback;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 오디오 모듈 한 벌(엔진, 출력 관리, 믹서, 원샷, 음악·곡 재생기, 곡 시계 원천, 포커스 정책).
    // AudioModuleInstaller가 만들고 ServiceLocator에 계약을 등록한다.
    // 장치 사건 처리(콜백은 system.update 안에서 오고 플래그로 받는다):
    //   DEVICELOST           → 재구성(직전 구성 → WASAPI 기본 → NOSOUND). 흐르는 곡은 장치 사유로 일시정지된다.
    //   DEVICEREINITIALIZE   → (Follow-Default에서 FMOD가 기본 장치를 바꿈) 흐르는 곡을 일시정지하고 곡 시계를 리셋한다.
    //   DEVICELISTCHANGED    → (Pinned만) 고정 장치가 목록에 없으면 1초 디바운스 뒤 같은 타입 기본 장치로 재구성한다.
    //                           장치가 돌아와도 자동으로 복귀하지 않는다(다음 부팅이나 재적용 때 복원).
    //   콜백 없는 정지       → 곡이 흐르는 중 포커스가 있는데 원시 DSP가 0.5초 넘게 그대로면 장치 사유로 일시정지한다.
    public sealed class AudioModule
    {
        private readonly AudioModuleOptions _options;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly AudioEngine _engine = new AudioEngine();
        private readonly FmodMixer _mixer = new FmodMixer();
        private readonly ClockSampler _sampler;
        private readonly OneShotBank _oneShots;
        private readonly MusicPlayers _music;
        private readonly SongPlayer _songPlayer;
        private readonly SongMetronome _metronome;
        private readonly FocusPolicy _focus;
        private readonly DeviceCatalog _catalog;
        private readonly AudioOutputService _output;
        private readonly LegacyTransport _legacy;
        private bool _isShutDown;
        private long _pinnedMissingSinceQpc;

        public const double PinnedMissingDebounceSeconds = 1.0;
        public const double StallSeconds = 0.5;

        internal AudioModule(AudioModuleOptions options)
        {
            _options = options;
            _sampler = new ClockSampler(_engine, _mixer);
            _oneShots = new OneShotBank(options.HitSoundFolder);
            var lobby = new FmodMusicPlayer(_engine, _mixer, options.MusicFolder, 0, _cts.Token);
            var preview = new FmodMusicPlayer(_engine, _mixer, options.MusicFolder, MusicPlayers.PreviewDebounceSeconds, _cts.Token);
            _music = new MusicPlayers(lobby, preview);
            _songPlayer = new SongPlayer(_engine, _mixer, _sampler, options.MusicFolder, _cts.Token);
            _metronome = new SongMetronome(_engine, _mixer, _sampler);
            _focus = new FocusPolicy(_mixer, _songPlayer, options.PlayInBackground, options.PauseSongOnFocusLoss);
            _catalog = new DeviceCatalog(_engine);
            _output = new AudioOutputService(_engine, _catalog, BlockingReason, Reconfigure, _cts.Token);
            _legacy = new LegacyTransport(_engine, _mixer, _oneShots, _catalog, _output, options.MusicFolder);
        }

        public IAudioEngine Engine
        {
            get { return _engine; }
        }

        public IAudioMixer Mixer
        {
            get { return _mixer; }
        }

        public IOneShotPlayer OneShots
        {
            get { return _oneShots; }
        }

        public IMusicPlayers Music
        {
            get { return _music; }
        }

        public ISongPlayer Songs
        {
            get { return _songPlayer; }
        }

        public IAudioOutputService Output
        {
            get { return _output; }
        }

        public double LastDeviceEnumerateMilliseconds
        {
            get { return _catalog.LastEnumerateMilliseconds; }
        }

        // 진단용(하네스·개발 빌드).
        // [과도기] 옛 IAudioManager 어댑터(App)만 쓴다. C 단계에서 삭제한다.
        public ILegacyTransport Legacy
        {
            get { return _legacy; }
        }

        public SongMetronome Metronome
        {
            get { return _metronome; }
        }

        public bool IsShutDown
        {
            get { return _isShutDown; }
        }

        public string BootSummary
        {
            get { return _engine.BootSummary; }
        }

        public double InitMilliseconds
        {
            get { return _engine.InitMilliseconds; }
        }

        public int TotalFmodErrors
        {
            get { return _engine.TotalErrors; }
        }

        internal void Boot(bool safeMode)
        {
            _engine.Boot(_options.Output, safeMode, AsioPolicy.IsAllowed);
            BuildResources();
            _legacy.OnEngineOpened();
        }

        // 같은 구성으로 close → init 한다(진단·하네스용 강제 재구성). 설정 적용은 Output.ApplyAsync가 맡는다.
        public void Reinitialize()
        {
            BootAttempt current = _engine.CurrentAttempt;
            Reconfigure(BootPlan.BuildRecovery(current, current), "강제 재구성");
        }

        // 순서: 재생기·세션에 알림(흐르는 곡은 일시정지) → 채널·Sound·ChannelGroup 해제 → close·init → 다시 구성 → 되살리기.
        // 성공한 시도의 번호를 돌려준다(모두 실패하면 -1).
        internal int Reconfigure(IReadOnlyList<BootAttempt> attempts, string reason)
        {
            AudioThread.AssertMain("AudioModule.Reconfigure");
            if (_isShutDown) return -1;

            _songPlayer.OnEngineClosing();
            _music.OnEngineClosing();
            _legacy.OnEngineClosing();
            _metronome.Release();
            _oneShots.Release();
            _mixer.Release();

            int index = _engine.Reinitialize(attempts, reason);
            BuildResources();
            _pinnedMissingSinceQpc = 0;

            _songPlayer.OnEngineOpened();
            _music.OnEngineOpened();
            _legacy.OnEngineOpened();
            return index;
        }

        // 설정 적용을 막는 사유. 곡이 진행 중(Ready·Stopped·Disposed가 아님)이면 적용하지 않는다.
        private string BlockingReason()
        {
            ISongSession session = _songPlayer.Current;
            if (session == null) return null;
            SongSessionState state = session.State;
            if (state == SongSessionState.Ready || state == SongSessionState.Stopped || state == SongSessionState.Disposed) return null;
            return "곡 진행 중(" + state + ")";
        }

        // 믹서를 먼저 만들고, 곡 시계 원천을 리셋한 뒤, 원샷을 타격음 버스에 붙인다.
        private void BuildResources()
        {
            string error = _mixer.Build(_engine);
            if (error != null) Debug.LogError("[Audio] 믹서 구성 실패: " + error);
            _sampler.Reset();
            _oneShots.Bind(_engine, _mixer.HitSoundGroup);
        }

        // 매 프레임 Update(-1010): FMOD update → 곡 시계 샘플 → 곡 세션(프레임 스냅샷, 커밋, 상태 전이) → 진단 메트로놈.
        internal void Tick()
        {
            if (_isShutDown) return;
            _engine.Update();
            HandleDeviceEvents();
            _sampler.Sample();
            DetectSilentStall();
            _songPlayer.Tick();
            _legacy.Tick();
            _metronome.Tick(_songPlayer.CurrentInternal);
        }

        private void HandleDeviceEvents()
        {
            CallbackSnapshot callbacks = _engine.Callbacks;
            bool lost = callbacks.DeviceLost;
            bool reinitialized = callbacks.DeviceReinitialized;
            bool listChanged = callbacks.DeviceListChanged;

            if (lost)
            {
                BootAttempt current = _engine.CurrentAttempt;
                Reconfigure(BootPlan.BuildRecovery(current, current), "장치 손실");
                return;
            }

            if (reinitialized)
            {
                // FMOD가 기본 장치를 바꿨다. 흐르는 곡은 멈추고(재개는 사용자가) 곡 시계를 새로 맞춘다.
                PauseRunningSong(PauseReason.DeviceChanged);
                _engine.RefreshCurrentDevice();
                _sampler.Reset();
            }

            BootAttempt attempt = _engine.CurrentAttempt;
            bool pinned = _engine.IsUsable && attempt.DeviceId != Guid.Empty;
            if (listChanged && pinned)
            {
                _catalog.Enumerate(attempt.Kind);
                if (_catalog.Contains(attempt.Kind, attempt.DeviceId)) _pinnedMissingSinceQpc = 0;
                else if (_pinnedMissingSinceQpc == 0) _pinnedMissingSinceQpc = Qpc.Now;
            }

            if (_pinnedMissingSinceQpc != 0 && Qpc.ToSeconds(Qpc.Now - _pinnedMissingSinceQpc) >= PinnedMissingDebounceSeconds)
            {
                _pinnedMissingSinceQpc = 0;
                _catalog.Enumerate(attempt.Kind);
                if (pinned && !_catalog.Contains(attempt.Kind, attempt.DeviceId))
                {
                    Debug.LogWarning("[Audio] 고정한 출력 장치가 사라져 기본 장치로 전환합니다: " + attempt.DeviceName);
                    var fallback = new BootAttempt(attempt.Kind, Guid.Empty, "", attempt.BufferLength, attempt.BufferCount, "같은 타입 기본 장치");
                    Reconfigure(BootPlan.BuildRecovery(fallback, attempt), "고정 장치 사라짐");
                }
            }
        }

        private void DetectSilentStall()
        {
            if (!_engine.IsUsable || _sampler.IsVirtual || !Application.isFocused) return;
            if (_sampler.SecondsSinceRawChange <= StallSeconds) return;
            ISongSession session = _songPlayer.Current;
            if (session == null) return;
            SongSessionState state = session.State;
            if (state != SongSessionState.Starting && state != SongSessionState.LeadIn && state != SongSessionState.Playing) return;
            Debug.LogWarning("[Audio] 믹서 클록이 " + StallSeconds + "초 넘게 멈춰 곡을 일시정지합니다.");
            session.Pause(PauseReason.DeviceChanged);
        }

        private void PauseRunningSong(PauseReason reason)
        {
            ISongSession session = _songPlayer.Current;
            if (session != null) session.Pause(reason);
        }

        // LateUpdate: 샘플을 한 번 더 넣어 프레임 안 읽기 위상을 늘린다(하한 포락선 정밀도).
        internal void LateTick()
        {
            if (_isShutDown) return;
            _sampler.Sample();
        }

        internal void OnFocusChanged(bool hasFocus)
        {
            if (_isShutDown) return;
            _focus.OnFocusChanged(hasFocus);
        }

        // 멱등. 순서: 모듈 CTS 취소 → 콜백 해제 → 채널·Sound·ChannelGroup 해제 → System release → FMOD Debug 복구 → 등록 해제.
        public void Shutdown()
        {
            if (_isShutDown) return;
            _isShutDown = true;
            _cts.Cancel();

            _engine.DetachCallbacks();
            _metronome.Detach();
            _metronome.Release();
            _songPlayer.Shutdown();
            _music.Shutdown();
            _legacy.Shutdown();
            _oneShots.Release();
            _mixer.Release();
            _engine.Shutdown();

            RemoveService<IAudioEngine>(_engine);
            RemoveService<IAudioMixer>(_mixer);
            RemoveService<IOneShotPlayer>(_oneShots);
            RemoveService<IMusicPlayers>(_music);
            RemoveService<ISongPlayer>(_songPlayer);
            RemoveService<IAudioOutputService>(_output);
            EditorAudioLifecycle.Unregister(this);
            Debug.Log("[Audio] 모듈을 종료했습니다.");
        }

        internal void RegisterServices()
        {
            ServiceLocator.Register<IAudioEngine>(_engine);
            ServiceLocator.Register<IAudioMixer>(_mixer);
            ServiceLocator.Register<IOneShotPlayer>(_oneShots);
            ServiceLocator.Register<IMusicPlayers>(_music);
            ServiceLocator.Register<ISongPlayer>(_songPlayer);
            ServiceLocator.Register<IAudioOutputService>(_output);
        }

        private static void RemoveService<T>(T instance) where T : class
        {
            T registered;
            if (ServiceLocator.TryGet(out registered) && ReferenceEquals(registered, instance)) ServiceLocator.Remove<T>();
        }
    }
}
