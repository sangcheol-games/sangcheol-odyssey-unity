using System.Threading;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Diagnostics;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Playback;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 오디오 모듈 한 벌(엔진, 믹서, 원샷, 음악·곡 재생기, 곡 시계 원천, 포커스 정책).
    // AudioModuleInstaller가 만들고 ServiceLocator에 계약을 등록한다.
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
        private bool _isShutDown;

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
            _focus = new FocusPolicy(_mixer, options.PlayInBackground);
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

        // 진단용(하네스·개발 빌드).
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
        }

        // 같은 구성으로 close → init 한다(진단·하네스용 강제 재구성). 설정 적용은 출력 관리 단계(S2b)의 ApplyAsync가 맡는다.
        // 순서: 재생기·세션에 알림(흐르는 곡은 일시정지) → 채널·Sound·ChannelGroup 해제 → close·init → 다시 구성 → 되살리기.
        public void Reinitialize()
        {
            AudioThread.AssertMain("AudioModule.Reinitialize");
            if (_isShutDown) return;

            _songPlayer.OnEngineClosing();
            _music.OnEngineClosing();
            _metronome.Release();
            _oneShots.Release();
            _mixer.Release();

            _engine.Reinitialize();
            BuildResources();

            _songPlayer.OnEngineOpened();
            _music.OnEngineOpened();
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
            _sampler.Sample();
            _songPlayer.Tick();
            _metronome.Tick(_songPlayer.CurrentInternal);
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
            _oneShots.Release();
            _mixer.Release();
            _engine.Shutdown();

            RemoveService<IAudioEngine>(_engine);
            RemoveService<IAudioMixer>(_mixer);
            RemoveService<IOneShotPlayer>(_oneShots);
            RemoveService<IMusicPlayers>(_music);
            RemoveService<ISongPlayer>(_songPlayer);
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
        }

        private static void RemoveService<T>(T instance) where T : class
        {
            T registered;
            if (ServiceLocator.TryGet(out registered) && ReferenceEquals(registered, instance)) ServiceLocator.Remove<T>();
        }
    }
}
