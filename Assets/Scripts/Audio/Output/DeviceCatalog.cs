using System;
using System.Collections.Generic;
using System.Diagnostics;
using SCOdyssey.Audio.Engine;

namespace SCOdyssey.Audio.Output
{
    // 출력 타입별 장치 목록(GUID). 현재 타입은 메인 System으로, 다른 타입은 초기화하지 않은 임시 System으로 열거하고
    // 곧바로 release한다. 메인이 ASIO면 임시 ASIO System을 만들지 않는다(ASIO는 프로세스당 하나).
    // "NoSound Driver"(Guid.Empty)는 목록에서 뺀다.
    internal sealed class DeviceCatalog
    {
        private static readonly IReadOnlyList<AudioDeviceInfo> Empty = new AudioDeviceInfo[0];

        private readonly AudioEngine _engine;
        private readonly Dictionary<AudioOutputKind, IReadOnlyList<AudioDeviceInfo>> _cache = new Dictionary<AudioOutputKind, IReadOnlyList<AudioDeviceInfo>>();

        public DeviceCatalog(AudioEngine engine)
        {
            _engine = engine;
        }

        public double LastEnumerateMilliseconds { get; private set; }
        public string LastError { get; private set; } = "";

        public IReadOnlyList<AudioDeviceInfo> GetCached(AudioOutputKind kind)
        {
            IReadOnlyList<AudioDeviceInfo> list;
            if (_cache.TryGetValue(kind, out list)) return list;
            return Empty;
        }

        public IReadOnlyList<AudioDeviceInfo> Enumerate(AudioOutputKind kind)
        {
            AudioThread.AssertMain("DeviceCatalog.Enumerate");
            if (kind == AudioOutputKind.NoSound) return Empty;
            if (kind == AudioOutputKind.Asio && !AsioPolicy.IsSupported) return Empty;

            var watch = Stopwatch.StartNew();
            List<AudioDeviceInfo> devices;
            // 메인이 같은 타입으로 동작 중이면 메인으로 읽는다(메인이 ASIO일 때 임시 ASIO를 만들지 않는 것도 여기서 보장된다).
            if (_engine.IsUsable && _engine.CurrentOutput.Kind == kind) devices = ReadAll(_engine.CoreSystem);
            else devices = ReadWithTemporarySystem(kind);
            LastEnumerateMilliseconds = watch.Elapsed.TotalMilliseconds;
            _cache[kind] = devices;
            return devices;
        }

        public bool Contains(AudioOutputKind kind, Guid deviceId)
        {
            IReadOnlyList<AudioDeviceInfo> list = GetCached(kind);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == deviceId) return true;
            }
            return false;
        }

        private List<AudioDeviceInfo> ReadWithTemporarySystem(AudioOutputKind kind)
        {
            var devices = new List<AudioDeviceInfo>();
            FMOD.RESULT result = FMOD.Factory.System_Create(out FMOD.System temp);
            if (result != FMOD.RESULT.OK)
            {
                LastError = "System_Create: " + result;
                return devices;
            }
            try
            {
                result = temp.setOutput(EngineConfigurator.ToFmod(kind));
                if (result != FMOD.RESULT.OK)
                {
                    LastError = "setOutput: " + result;
                    return devices;
                }
                devices = ReadAll(temp);
            }
            finally
            {
                temp.release();
            }
            return devices;
        }

        private List<AudioDeviceInfo> ReadAll(FMOD.System system)
        {
            var devices = new List<AudioDeviceInfo>();
            FMOD.RESULT result = system.getNumDrivers(out int count);
            if (result != FMOD.RESULT.OK)
            {
                LastError = "getNumDrivers: " + result;
                return devices;
            }
            for (int i = 0; i < count; i++)
            {
                AudioDeviceInfo info;
                if (!DriverLookup.TryRead(system, i, out info)) continue;
                if (info.Id == Guid.Empty) continue;
                devices.Add(info);
            }
            LastError = "";
            return devices;
        }
    }
}
