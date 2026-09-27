using System;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Playback;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 오디오 모듈 한 벌(엔진, 믹서, 원샷, 포커스 정책). AudioModuleInstaller가 만들고 ServiceLocator에 계약을 등록한다.
    public sealed class AudioModule
    {
        private readonly AudioModuleOptions _options;
        private readonly AudioEngine _engine = new AudioEngine();
        private readonly FmodMixer _mixer = new FmodMixer();
        private readonly OneShotBank _oneShots;
        private readonly FocusPolicy _focus;
        private bool _isShutDown;

        internal AudioModule(AudioModuleOptions options)
        {
            _options = options;
            _oneShots = new OneShotBank(options.HitSoundFolder);
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

        internal AudioEngine EngineInternal
        {
            get { return _engine; }
        }

        internal FmodMixer MixerInternal
        {
            get { return _mixer; }
        }

        internal void Boot(bool safeMode)
        {
            _engine.Boot(_options.Output, safeMode, AsioPolicy.IsAllowed);
            BuildResources();
        }

        // 믹서를 먼저 만들고 원샷을 타격음 버스에 붙인다. 재구성(S2b) 뒤에도 같은 순서로 다시 부른다.
        internal void BuildResources()
        {
            string error = _mixer.Build(_engine);
            if (error != null) Debug.LogError("[Audio] 믹서 구성 실패: " + error);
            _oneShots.Bind(_engine, _mixer.HitSoundGroup);
        }

        internal void Tick()
        {
            if (_isShutDown) return;
            _engine.Update();
        }

        internal void OnFocusChanged(bool hasFocus)
        {
            if (_isShutDown) return;
            _focus.OnFocusChanged(hasFocus);
        }

        // 멱등. 순서: 콜백 해제 → Sound·ChannelGroup 해제 → System release → FMOD Debug 복구 → 등록 해제.
        public void Shutdown()
        {
            if (_isShutDown) return;
            _isShutDown = true;

            _engine.DetachCallbacks();
            _oneShots.Release();
            _mixer.Release();
            _engine.Shutdown();

            RemoveService<IAudioEngine>(_engine);
            RemoveService<IAudioMixer>(_mixer);
            RemoveService<IOneShotPlayer>(_oneShots);
            EditorAudioLifecycle.Unregister(this);
            Debug.Log("[Audio] 모듈을 종료했습니다.");
        }

        internal void RegisterServices()
        {
            ServiceLocator.Register<IAudioEngine>(_engine);
            ServiceLocator.Register<IAudioMixer>(_mixer);
            ServiceLocator.Register<IOneShotPlayer>(_oneShots);
        }

        private static void RemoveService<T>(T instance) where T : class
        {
            T registered;
            if (ServiceLocator.TryGet(out registered) && ReferenceEquals(registered, instance)) ServiceLocator.Remove<T>();
        }
    }
}
