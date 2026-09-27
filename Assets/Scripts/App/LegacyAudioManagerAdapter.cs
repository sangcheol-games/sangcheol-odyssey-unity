using SCOdyssey.Audio;
using SCOdyssey.Audio.Hosting;
using SCOdyssey.Audio.Legacy;
using SCOdyssey.Core;

namespace SCOdyssey.App
{
    // [과도기] 옛 IAudioManager를 새 오디오 모듈 위에서 제공한다(S4a~C). 호출은 ILegacyTransport와 IAudioMixer로 전달만 한다.
    // 모듈 설치에 실패하면 소리 없는 어댑터를 등록한다: IsLoaded는 곧바로 true, GetDSPTime은 QPC로 진행, 나머지는 무시.
    // 모든 소비자(MainUI, AdventureUI, GameDataLoader, GameManager, ChartManager, SoundSettingUI)를 옮기면 C 단계에서 삭제한다.
    public sealed class LegacyAudioManagerAdapter : IAudioManager
    {
        private static readonly string[] NoDevices = new string[0];

        private readonly ILegacyTransport _transport;
        private readonly IAudioMixer _mixer;
        private readonly long _silentOriginQpc = Qpc.Now;

        private LegacyAudioManagerAdapter(ILegacyTransport transport, IAudioMixer mixer)
        {
            _transport = transport;
            _mixer = mixer;
        }

        public static LegacyAudioManagerAdapter RegisterInto(AudioModule module)
        {
            var adapter = new LegacyAudioManagerAdapter(module.Legacy, module.Mixer);
            ServiceLocator.TryRegister<IAudioManager>(adapter);
            return adapter;
        }

        public static LegacyAudioManagerAdapter RegisterSilent()
        {
            var adapter = new LegacyAudioManagerAdapter(null, null);
            ServiceLocator.TryRegister<IAudioManager>(adapter);
            return adapter;
        }

        public bool IsLoaded
        {
            get
            {
                if (_transport == null) return true;
                return _transport.IsLoaded;
            }
        }

        public bool IsPlaying
        {
            get
            {
                if (_transport == null) return false;
                return _transport.IsPlaying;
            }
        }

        public void LoadAudio(string filePath, bool loopHint = false)
        {
            if (_transport != null) _transport.Load(filePath, loopHint);
        }

        public void PlayScheduled(double dspStartTime, bool loopPlay = false)
        {
            if (_transport != null) _transport.PlayAt(dspStartTime, loopPlay);
        }

        public void Stop()
        {
            if (_transport != null) _transport.Stop();
        }

        public void Pause()
        {
            if (_transport != null) _transport.Pause();
        }

        public void Resume()
        {
            if (_transport != null) _transport.Resume();
        }

        public double GetDSPTime()
        {
            if (_transport == null) return Qpc.ToSeconds(Qpc.Now - _silentOriginQpc);
            return _transport.DspSeconds;
        }

        // 옛 API. 출력 설정은 부팅(AudioSettingsMapper)과 IAudioOutputService.ApplyAsync가 맡는다.
        public void ConfigureOutput(AudioOutputConfig config)
        {
        }

        public string[] GetAvailableDevices()
        {
            if (_transport == null) return NoDevices;
            return _transport.GetDeviceNames();
        }

        public void SetAudioDevice(int driverIndex)
        {
            if (_transport != null) _transport.SelectDevice(driverIndex);
        }

        public int RegisterOneShot(string fileName)
        {
            if (_transport == null) return -1;
            return _transport.RegisterOneShot(fileName);
        }

        // bus와 volume은 쓰는 곳이 없어 무시한다(원샷은 모두 타격음 버스).
        public void PlayOneShot(int slot, AudioBus bus = AudioBus.HitSound, float volume = 1f)
        {
            if (_transport != null) _transport.PlayOneShot(slot);
        }

        public void SetMasterVolume(float volume)
        {
            if (_mixer != null) _mixer.Master.Volume = volume;
        }

        public void SetBgmVolume(float volume)
        {
            if (_mixer != null) _mixer.Music.Volume = volume;
        }

        public void SetHitSoundVolume(float volume)
        {
            if (_mixer != null) _mixer.HitSound.Volume = volume;
        }

        public void SetSfxVolume(float volume)
        {
            if (_mixer != null) _mixer.Sfx.Volume = volume;
        }
    }
}
