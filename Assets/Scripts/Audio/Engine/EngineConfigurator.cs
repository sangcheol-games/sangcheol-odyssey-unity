using System;
using SCOdyssey.Audio.Output;

namespace SCOdyssey.Audio.Engine
{
    // 부팅 시도 한 번의 설정·init·검증(Audio_architecture.md 4장 순서). 모두 메인 스레드에서 부른다.
    internal static class EngineConfigurator
    {
        public const int SoftwareChannels = 64;
        public const int MaxChannels = 256;
        public const int MaxWasapiMixerRate = 48000;

        // 성공하면 actual에 검증한 실제 구성을 담고 null을 돌려준다. 실패하면 사유를 돌려준다.
        public static string TryInit(FMOD.System system, BootAttempt attempt, out AudioOutputInfo actual)
        {
            actual = default;
            FMOD.OUTPUTTYPE output = ToFmod(attempt.Kind);

            FMOD.RESULT result = system.setOutput(output);
            if (result != FMOD.RESULT.OK) return "setOutput: " + result;

            int driver = 0;
            string deviceName = "";
            int systemRate = 0;
            Guid deviceGuid = Guid.Empty;
            if (attempt.Kind != AudioOutputKind.NoSound)
            {
                result = system.getNumDrivers(out int driverCount);
                if (result != FMOD.RESULT.OK) return "getNumDrivers: " + result;
                if (driverCount <= 0) return "출력 장치 없음";

                driver = DriverLookup.ResolveIndex(system, attempt.DeviceId, attempt.DeviceName);
                result = system.setDriver(driver);
                if (result != FMOD.RESULT.OK) return "setDriver: " + result;

                result = system.getDriverInfo(driver, out deviceName, 256, out deviceGuid, out systemRate, out _, out _);
                if (result != FMOD.RESULT.OK) return "getDriverInfo: " + result;
            }

            // WASAPI는 48kHz를 넘기지 않는다(96/192kHz 장치에서 블록이 너무 짧아지는 것 방지). ASIO는 드라이버 레이트를 따른다.
            int mixerRate = systemRate;
            if (mixerRate <= 0) mixerRate = MaxWasapiMixerRate;
            if (attempt.Kind != AudioOutputKind.Asio && mixerRate > MaxWasapiMixerRate) mixerRate = MaxWasapiMixerRate;

            result = system.setSoftwareFormat(mixerRate, FMOD.SPEAKERMODE.STEREO, 0);
            if (result != FMOD.RESULT.OK) return "setSoftwareFormat: " + result;

            result = system.setDSPBufferSize((uint)attempt.BufferLength, attempt.BufferCount);
            if (result != FMOD.RESULT.OK) return "setDSPBufferSize: " + result;

            result = system.setSoftwareChannels(SoftwareChannels);
            if (result != FMOD.RESULT.OK) return "setSoftwareChannels: " + result;

            result = SystemCallbackHub.Install(system, attempt.DeviceId != Guid.Empty);
            if (result != FMOD.RESULT.OK) return "setCallback: " + result;

            result = system.init(MaxChannels, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
            if (result != FMOD.RESULT.OK) return "init: " + result;

            // 결과가 OK여도 실제 상태가 요청과 다를 수 있으므로 다시 읽어 확인한다.
            system.getOutput(out FMOD.OUTPUTTYPE actualOutput);
            if (actualOutput != output) return "실제 출력이 요청과 다름: " + actualOutput;

            if (attempt.Kind != AudioOutputKind.NoSound)
            {
                system.getDriver(out int actualDriver);
                if (actualDriver != driver) return "실제 장치 번호가 요청과 다름: " + actualDriver;
            }

            // 버퍼와 레이트는 드라이버가 정할 수 있다(ASIO는 제어판 버퍼를 따름). 실패로 보지 않고 실제값을 쓴다.
            system.getDSPBufferSize(out uint actualLength, out int actualCount);
            system.getSoftwareFormat(out int actualRate, out _, out _);

            Guid reportedId = attempt.DeviceId;
            actual = new AudioOutputInfo(attempt.Kind, reportedId, deviceName, actualRate, (int)actualLength, actualCount);
            return null;
        }

        public static FMOD.OUTPUTTYPE ToFmod(AudioOutputKind kind)
        {
            if (kind == AudioOutputKind.Asio) return FMOD.OUTPUTTYPE.ASIO;
            if (kind == AudioOutputKind.NoSound) return FMOD.OUTPUTTYPE.NOSOUND;
            return FMOD.OUTPUTTYPE.WASAPI;
        }
    }
}
