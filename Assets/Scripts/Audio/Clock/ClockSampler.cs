using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Core;

namespace SCOdyssey.Audio.Clock
{
    // 곡 시계의 DSP 원천. 매 프레임 Update·LateUpdate에서 (QPC 앞, DSP, QPC 뒤)를 읽어 모델에 넣는다.
    // DSP는 SCO.Song 그룹의 클록이다. 곡 채널의 setDelay가 이 도메인이므로 커밋과 곡 시계가 같은 값을 쓴다.
    // 엔진을 쓸 수 없으면(Degraded·Failed) QPC로 흐르는 가상 클록을 넣어 무음 세션도 같은 경로로 진행한다.
    internal sealed class ClockSampler
    {
        public const int VirtualSampleRate = 48000;
        public const int VirtualBlockLength = 512;
        private const int VirtualBlockCount = 4;

        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly DspQpcModel _model = new DspQpcModel(VirtualSampleRate, VirtualBlockLength, VirtualBlockCount);
        private bool _isVirtual = true;
        private long _virtualOriginQpc;
        private int _blockLength = VirtualBlockLength;

        public ClockSampler(AudioEngine engine, FmodMixer mixer)
        {
            _engine = engine;
            _mixer = mixer;
        }

        public DspQpcModel Model
        {
            get { return _model; }
        }

        public int SampleRate
        {
            get { return _model.SampleRate; }
        }

        public int BlockLength
        {
            get { return _blockLength; }
        }

        public bool IsVirtual
        {
            get { return _isVirtual; }
        }

        // 부팅·재구성 뒤 부른다(모델 하드 리셋). 이전 모델로 만든 흐르는 세그먼트는 더 이상 계산되지 않는다.
        public void Reset()
        {
            AudioOutputInfo output = _engine.CurrentOutput;
            if (_engine.IsUsable && _mixer.SongGroup.hasHandle() && output.SampleRate > 0)
            {
                _isVirtual = false;
                _blockLength = output.BufferLength;
                _model.Reset(output.SampleRate, output.BufferLength, output.BufferCount);
            }
            else
            {
                _isVirtual = true;
                _blockLength = VirtualBlockLength;
                _virtualOriginQpc = Qpc.Now;
                _model.Reset(VirtualSampleRate, VirtualBlockLength, VirtualBlockCount);
            }
        }

        public void Sample()
        {
            long before = Qpc.Now;
            ulong dsp = ReadDsp();
            long after = Qpc.Now;
            _model.AddSample(before, dsp, after);
        }

        public ulong ReadDsp()
        {
            if (!_isVirtual && _mixer.SongGroup.hasHandle())
            {
                _mixer.SongGroup.getDSPClock(out ulong clock, out _);
                return clock;
            }
            return (ulong)((double)(Qpc.Now - _virtualOriginQpc) * VirtualSampleRate / Qpc.Frequency);
        }
    }
}
