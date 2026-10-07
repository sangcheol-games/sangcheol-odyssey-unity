using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Playback;

namespace SCOdyssey.Audio.Diagnostics
{
    // 진단용 메트로놈. 곡 시각 기준 박을 앞으로 0.2초 안에 올 것만 미래 DSP 시각에 예약한다(기본은 오른쪽 패닝).
    // 곡 음원(왼쪽 클릭 트랙)과 함께 루프백 녹음하면 앵커 커밋과 일시정지·재개 예약의 정확도를 잴 수 있다(SP4).
    // 곡 시계가 불연속이 되면(일시정지·재개 등) 예약해 둔 클릭을 모두 멈추고 새 세그먼트 기준으로 다시 예약한다.
    public sealed class SongMetronome
    {
        private const double HorizonSeconds = 0.2;
        private const int MaxPending = 32;
        private const double ClickSeconds = 0.005;
        private const double ClickFrequency = 2000.0;

        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly ClockSampler _sampler;
        private readonly FMOD.Channel[] _pending = new FMOD.Channel[MaxPending];
        private readonly ulong[] _pendingEndDsp = new ulong[MaxPending];      // 클릭이 끝나는 DSP. 끝난 채널 핸들은 무효이므로 건드리지 않는다
        private readonly Action<ClockDiscontinuity> _onDiscontinuity;
        private int _pendingNext;
        private FMOD.Sound _click;
        private FmodSongSession _session;
        private int _epoch = -1;
        private long _nextIndex;

        internal SongMetronome(AudioEngine engine, FmodMixer mixer, ClockSampler sampler)
        {
            _engine = engine;
            _mixer = mixer;
            _sampler = sampler;
            _onDiscontinuity = OnDiscontinuity;
        }

        public bool Enabled { get; set; }
        public double IntervalSeconds { get; set; } = 0.5;
        public double FirstBeatSongTime { get; set; } = 1.0;
        public double LastBeatSongTime { get; set; } = double.MaxValue;
        public float Pan { get; set; } = 1f;
        public int ScheduledClicks { get; private set; }
        public int LateClicks { get; private set; }

        public void ResetCounters()
        {
            ScheduledClicks = 0;
            LateClicks = 0;
        }

        internal void Tick(FmodSongSession session)
        {
            if (session != _session) Attach(session);
            if (!Enabled || session == null || !_engine.IsUsable || !_mixer.SongGroup.hasHandle())
            {
                StopPending();
                return;
            }

            SongFrame frame = session.Clock.Frame;
            if (!frame.IsRunning) return;
            if (frame.Epoch != _epoch)
            {
                _epoch = frame.Epoch;
                _nextIndex = (long)Math.Ceiling((frame.SongTime - FirstBeatSongTime) / IntervalSeconds);
                if (_nextIndex < 0) _nextIndex = 0;
            }
            if (!EnsureClick()) return;

            ulong now = _sampler.ReadDsp();
            ulong horizon = now + (ulong)Math.Round(HorizonSeconds * _sampler.SampleRate);
            ulong tooLate = now + (ulong)_sampler.BlockLength;
            while (true)
            {
                double beat = FirstBeatSongTime + _nextIndex * IntervalSeconds;
                if (beat > LastBeatSongTime) break;
                ulong dsp;
                if (!session.TryDspForSongTime(beat, out dsp)) break;
                if (dsp > horizon) break;

                // 이미 믹스 커서가 지나간 시각이면 늦은 예약으로 세고 건너뛴다.
                if (dsp <= tooLate) LateClicks++;
                else Schedule(dsp);
                _nextIndex++;
            }
        }

        // 재구성·종료 전에 부른다(System close 전에 채널과 Sound를 해제해야 한다).
        internal void Release()
        {
            StopPending();
            if (_click.hasHandle()) _click.release();
            _click = default;
        }

        internal void Detach()
        {
            Attach(null);
        }

        private void Attach(FmodSongSession session)
        {
            if (_session != null) _session.ClockInternal.Discontinuity -= _onDiscontinuity;
            StopPending();
            _session = session;
            _epoch = -1;
            if (_session != null) _session.ClockInternal.Discontinuity += _onDiscontinuity;
        }

        private void OnDiscontinuity(ClockDiscontinuity discontinuity)
        {
            StopPending();
            _epoch = -1;
        }

        private void Schedule(ulong dsp)
        {
            FMOD.RESULT result = _engine.CoreSystem.playSound(_click, _mixer.SongGroup, true, out FMOD.Channel channel);
            if (result != FMOD.RESULT.OK) return;
            channel.setPan(Pan);
            channel.setDelay(dsp, 0, false);
            channel.setPaused(false);

            StopIfPending(_pendingNext, _sampler.ReadDsp());
            _pending[_pendingNext] = channel;
            _pendingEndDsp[_pendingNext] = dsp + ClickSamples();
            _pendingNext = (_pendingNext + 1) % MaxPending;
            ScheduledClicks++;
        }

        private void StopPending()
        {
            ulong now = 0;
            if (_engine.IsUsable) now = _sampler.ReadDsp();
            for (int i = 0; i < MaxPending; i++) StopIfPending(i, now);
        }

        // 아직 끝나지 않은(예약 대기 또는 재생 중) 클릭만 멈춘다. 끝난 채널 핸들에 stop을 부르면
        // FMOD가 ERR_INVALID_HANDLE·ERR_CHANNEL_STOLEN 오류 콜백을 낸다.
        private void StopIfPending(int slot, ulong now)
        {
            if (_pending[slot].hasHandle() && _engine.IsUsable && _pendingEndDsp[slot] > now) _pending[slot].stop();
            _pending[slot] = default;
            _pendingEndDsp[slot] = 0;
        }

        private ulong ClickSamples()
        {
            return (ulong)Math.Round(ClickSeconds * _sampler.SampleRate) + (ulong)_sampler.BlockLength;
        }

        private bool EnsureClick()
        {
            if (_click.hasHandle()) return true;
            byte[] data = BuildClickWav(_sampler.SampleRate);
            var info = new FMOD.CREATESOUNDEXINFO();
            info.cbsize = Marshal.SizeOf(info);
            info.length = (uint)data.Length;
            FMOD.MODE mode = FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF;
            FMOD.RESULT result = _engine.CoreSystem.createSound(data, mode, ref info, out _click);
            if (result == FMOD.RESULT.OK) return true;
            _click = default;
            return false;
        }

        // 시작이 날카로운 5ms 2kHz 버스트(모노 PCM16 WAV).
        private static byte[] BuildClickWav(int sampleRate)
        {
            int frames = (int)Math.Round(ClickSeconds * sampleRate);
            int dataBytes = frames * 2;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int i = 0; i < frames; i++)
                {
                    double value = 0.8 * Math.Sin(2.0 * Math.PI * ClickFrequency * i / sampleRate);
                    writer.Write((short)(value * short.MaxValue));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
