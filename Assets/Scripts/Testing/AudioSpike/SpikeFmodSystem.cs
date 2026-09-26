#if SCO_AUDIO_HARNESS
using System;
using System.Diagnostics;

namespace SCOdyssey.Testing.AudioSpike
{
    public struct SpikeOutputConfig
    {
        public FMOD.OUTPUTTYPE Output;
        public int DriverIndex;
        public uint BufferLength;
        public int BufferCount;
    }

    // 스파이크 전용 FMOD Core System 한 개. RuntimeManager를 거치지 않고 직접 만들고 해제한다.
    // 초기화 순서는 설계 문서(Audio_architecture.md 4장)와 같다.
    public sealed class SpikeFmodSystem : IDisposable
    {
        public FMOD.System CoreSystem;
        public FMOD.ChannelGroup Master;
        public FMOD.OUTPUTTYPE Output;
        public string DriverName = "";
        public int DriverRate;
        public int MixerRate;
        public uint BufferLength;
        public int BufferCount;
        public double InitSeconds;

        private SpikeOutputConfig _config;

        public static SpikeFmodSystem Create(SpikeOutputConfig config, out string error)
        {
            var created = new SpikeFmodSystem();
            created._config = config;
            var watch = Stopwatch.StartNew();

            FMOD.RESULT result = FMOD.Factory.System_Create(out created.CoreSystem);
            if (result != FMOD.RESULT.OK)
            {
                error = "System_Create: " + result;
                return null;
            }

            error = created.ConfigureAndInit();
            if (error != null)
            {
                created.Dispose();
                return null;
            }

            created.InitSeconds = watch.Elapsed.TotalSeconds;
            return created;
        }

        // 같은 System을 close → init 한다(설정 적용 경로와 같은 방식).
        public string CloseAndReinit()
        {
            FMOD.RESULT result = CoreSystem.close();
            if (result != FMOD.RESULT.OK) return "close: " + result;
            return ConfigureAndInit();
        }

        public ulong ReadMasterClock()
        {
            Master.getDSPClock(out ulong clock, out _);
            return clock;
        }

        public double BlockSeconds
        {
            get
            {
                if (MixerRate <= 0) return 0;
                return (double)BufferLength / MixerRate;
            }
        }

        public string Describe()
        {
            return string.Format("출력: {0}, 장치: {1}, 장치 레이트: {2}, 믹서 레이트: {3}, 버퍼: {4}x{5}",
                Output, DriverName, DriverRate, MixerRate, BufferLength, BufferCount);
        }

        public void Dispose()
        {
            if (!CoreSystem.hasHandle()) return;
            CoreSystem.setCallback(null, 0);
            CoreSystem.release();
            CoreSystem = default;
            Master = default;
        }

        private string ConfigureAndInit()
        {
            FMOD.RESULT result = CoreSystem.setOutput(_config.Output);
            if (result != FMOD.RESULT.OK) return "setOutput: " + result;

            result = CoreSystem.getNumDrivers(out int driverCount);
            if (result != FMOD.RESULT.OK) return "getNumDrivers: " + result;
            if (driverCount <= 0) return "장치 없음(getNumDrivers = 0)";

            int driver = _config.DriverIndex;
            if (driver < 0 || driver >= driverCount) driver = 0;

            result = CoreSystem.setDriver(driver);
            if (result != FMOD.RESULT.OK) return "setDriver: " + result;

            result = CoreSystem.getDriverInfo(driver, out DriverName, 256, out _, out DriverRate, out _, out _);
            if (result != FMOD.RESULT.OK) return "getDriverInfo: " + result;

            // WASAPI는 48kHz를 넘기지 않는다(96/192kHz 장치에서 블록이 너무 짧아지는 것 방지). ASIO는 드라이버 레이트를 따른다.
            MixerRate = DriverRate;
            if (MixerRate <= 0) MixerRate = 48000;
            if (_config.Output != FMOD.OUTPUTTYPE.ASIO && MixerRate > 48000) MixerRate = 48000;

            result = CoreSystem.setSoftwareFormat(MixerRate, FMOD.SPEAKERMODE.STEREO, 0);
            if (result != FMOD.RESULT.OK) return "setSoftwareFormat: " + result;

            result = CoreSystem.setDSPBufferSize(_config.BufferLength, _config.BufferCount);
            if (result != FMOD.RESULT.OK) return "setDSPBufferSize: " + result;

            result = CoreSystem.setSoftwareChannels(64);
            if (result != FMOD.RESULT.OK) return "setSoftwareChannels: " + result;

            result = CoreSystem.init(256, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
            if (result != FMOD.RESULT.OK) return "init: " + result;

            // 결과가 OK여도 실제 상태가 요청과 다를 수 있으므로 다시 읽어 확인한다.
            CoreSystem.getOutput(out Output);
            if (Output != _config.Output) return "실제 출력이 요청과 다름: " + Output;

            CoreSystem.getDSPBufferSize(out BufferLength, out BufferCount);
            CoreSystem.getSoftwareFormat(out MixerRate, out _, out _);

            result = CoreSystem.getMasterChannelGroup(out Master);
            if (result != FMOD.RESULT.OK) return "getMasterChannelGroup: " + result;

            return null;
        }
    }
}
#endif
