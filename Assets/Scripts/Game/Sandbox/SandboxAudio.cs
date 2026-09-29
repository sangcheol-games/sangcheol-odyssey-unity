using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FMODUnity;
using UnityEngine;

namespace SCOdyssey.Game
{
    public enum ClickKind { Hit, Beat, Downbeat }

    // 샌드박스 전용 소리(FMOD). 프로젝트는 Unity 오디오가 꺼져 있다.
    // 샌드박스 시계가 기준이고, 소리는 Anchor 때마다 "시계 t = DSP d" 대응을 새로 잡아 DSP 클록으로 예약 재생한다.
    public sealed class SandboxAudio : IDisposable
    {
        private const double LeadSec = 0.06;   // 예약 여유. 믹스 블록보다 길어야 샘플 단위로 맞는다

        private readonly FMOD.ChannelGroup _systemMaster;
        private readonly FMOD.ChannelGroup _group;
        private readonly FMOD.ChannelGroup _clickGroup;
        private readonly FMOD.Sound[] _clicks = new FMOD.Sound[3];
        private readonly int _sampleRate;

        private FMOD.Sound _music;
        private FMOD.Channel _musicChannel;
        private string _musicFile;
        private float _musicFrequency;

        private double _dspAnchor;
        private double _clockAnchor;
        private double _speed = 1;
        private bool _running;

        public SandboxAudio()
        {
            FMOD.System core = RuntimeManager.CoreSystem;
            core.getMasterChannelGroup(out _systemMaster);
            core.getSoftwareFormat(out _sampleRate, out _, out _);
            core.createChannelGroup("JudgeSandbox", out _group);
            core.createChannelGroup("JudgeSandboxClicks", out _clickGroup);
            _group.addGroup(_clickGroup, false, out _);

            _clicks[(int)ClickKind.Hit] = CreateClick(1900, 0.035, 0.55f);
            _clicks[(int)ClickKind.Beat] = CreateClick(1000, 0.03, 0.3f);
            _clicks[(int)ClickKind.Downbeat] = CreateClick(1500, 0.04, 0.5f);
        }

        public bool HasMusic => _music.hasHandle();
        public string MusicFile => _musicFile;

        public double DspNow
        {
            get
            {
                _systemMaster.getDSPClock(out ulong clock, out _);
                return (double)clock / _sampleRate;
            }
        }

        public void SetVolume(float volume) => _group.setVolume(Mathf.Clamp01(volume));

        // StreamingAssets/Music/ 기준 파일명
        public bool LoadMusic(string fileName)
        {
            if (fileName == _musicFile && HasMusic) return true;
            UnloadMusic();
            if (string.IsNullOrEmpty(fileName)) return false;

            string path = Path.Combine(Application.streamingAssetsPath, "Music", fileName);
            var exInfo = new FMOD.CREATESOUNDEXINFO { cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>() };
            FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(path, FMOD.MODE.CREATESTREAM, ref exInfo, out _music);
            if (result != FMOD.RESULT.OK)
            {
                Debug.LogWarning($"[JudgeSandbox] 음악을 열 수 없다: {result} ({path})");
                _music = default;
                return false;
            }

            _music.getDefaults(out _musicFrequency, out _);
            _musicFile = fileName;
            return true;
        }

        public void UnloadMusic()
        {
            StopMusic();
            if (_music.hasHandle()) _music.release();
            _music = default;
            _musicFile = null;
        }

        // 시계 clockNow를 지금 DSP에 대응시킨다. running이면 음악을 musicStartClock(곡 0초가 되는 시계 시각) 기준 위치에서 예약 재생한다.
        // 반환값: 이 시각 이후의 클릭만 예약할 수 있다
        public double Anchor(double clockNow, double speed, bool running, bool playMusic, double musicStartClock)
        {
            StopMusic();
            _clickGroup.stop();

            _running = running;
            _speed = speed;
            _dspAnchor = DspNow + LeadSec;
            _clockAnchor = clockNow + LeadSec * speed;

            if (running && playMusic && HasMusic) StartMusicAt(_clockAnchor - musicStartClock);
            return _clockAnchor;
        }

        public void Silence() => Anchor(_clockAnchor, _speed, false, false, 0);

        public void ScheduleClick(double clockTime, ClickKind kind)
        {
            if (!_running) return;
            double dsp = _dspAnchor + (clockTime - _clockAnchor) / _speed;
            if (dsp < DspNow) return;

            RuntimeManager.CoreSystem.playSound(_clicks[(int)kind], _clickGroup, true, out FMOD.Channel channel);
            channel.setDelay(ToDspClock(dsp), 0, false);
            channel.setPaused(false);
        }

        public void PlayClickNow(ClickKind kind)
            => RuntimeManager.CoreSystem.playSound(_clicks[(int)kind], _clickGroup, false, out _);

        // 음악이 시계보다 얼마나 앞서 있나(초). 재생 중이 아니면 0
        public double MusicDrift(double clockNow, double musicStartClock)
        {
            if (!_musicChannel.hasHandle()) return 0;
            _musicChannel.isPlaying(out bool playing);
            if (!playing) return 0;

            double expected = clockNow - musicStartClock;
            if (expected < 0.2) return 0;   // 예약 시작 전후는 재지 않는다
            _musicChannel.getPosition(out uint pcm, FMOD.TIMEUNIT.PCM);
            return pcm / (double)_musicFrequency - expected;
        }

        public void Dispose()
        {
            UnloadMusic();
            _clickGroup.stop();
            foreach (FMOD.Sound click in _clicks)
                if (click.hasHandle()) click.release();
            _clickGroup.release();
            _group.release();
        }

        private void StartMusicAt(double songPos)
        {
            _music.getLength(out uint lengthPcm, FMOD.TIMEUNIT.PCM);
            if (songPos * _musicFrequency >= lengthPcm) return;

            double startDsp = songPos >= 0 ? _dspAnchor : _dspAnchor + (-songPos) / _speed;
            RuntimeManager.CoreSystem.playSound(_music, _group, true, out _musicChannel);
            if (songPos > 0) _musicChannel.setPosition((uint)(songPos * _musicFrequency), FMOD.TIMEUNIT.PCM);
            _musicChannel.setPitch((float)_speed);
            _musicChannel.setDelay(ToDspClock(startDsp), 0, false);
            _musicChannel.setPaused(false);
        }

        private void StopMusic()
        {
            if (_musicChannel.hasHandle()) _musicChannel.stop();
            _musicChannel = default;
        }

        private ulong ToDspClock(double sec) => (ulong)(sec * _sampleRate);

        // 짧게 감쇠하는 사인파 클릭을 메모리 WAV로 만든다
        private FMOD.Sound CreateClick(double frequency, double durationSec, float amplitude)
        {
            const int rate = 48000;
            int samples = (int)(rate * durationSec);
            var pcm = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / rate;
                double envelope = Math.Exp(-t / (durationSec / 5));
                var value = (short)(amplitude * envelope * Math.Sin(2 * Math.PI * frequency * t) * short.MaxValue);
                pcm[i * 2] = (byte)value;
                pcm[i * 2 + 1] = (byte)(value >> 8);
            }

            byte[] wav = Wav(pcm, rate);
            var exInfo = new FMOD.CREATESOUNDEXINFO { cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(), length = (uint)wav.Length };
            RuntimeManager.CoreSystem.createSound(wav, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE, ref exInfo, out FMOD.Sound sound);
            return sound;
        }

        private static byte[] Wav(byte[] pcm16Mono, int rate)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm16Mono.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);      // PCM
            writer.Write((short)1);      // mono
            writer.Write(rate);
            writer.Write(rate * 2);      // byte rate
            writer.Write((short)2);      // block align
            writer.Write((short)16);     // bits
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm16Mono.Length);
            writer.Write(pcm16Mono);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
