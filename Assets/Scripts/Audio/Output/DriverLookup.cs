using System;

namespace SCOdyssey.Audio.Output
{
    // 출력 장치 번호를 찾는다. 저장과 비교는 GUID로 하고, 이름은 GUID로 못 찾을 때의 보조 키다.
    internal static class DriverLookup
    {
        private const int NameLength = 256;

        // GUID → 이름 → 0(기본 장치) 순서. Guid.Empty면 기본 장치 따라가기(Follow-Default)다.
        public static int ResolveIndex(FMOD.System system, Guid deviceId, string deviceName)
        {
            if (deviceId == Guid.Empty && string.IsNullOrEmpty(deviceName)) return 0;
            if (system.getNumDrivers(out int count) != FMOD.RESULT.OK) return 0;

            int byName = -1;
            for (int i = 0; i < count; i++)
            {
                FMOD.RESULT result = system.getDriverInfo(i, out string name, NameLength, out Guid guid, out _, out _, out _);
                if (result != FMOD.RESULT.OK) continue;
                if (deviceId != Guid.Empty && guid == deviceId) return i;
                if (byName < 0 && !string.IsNullOrEmpty(deviceName) && name == deviceName) byName = i;
            }
            if (byName >= 0) return byName;
            return 0;
        }

        public static bool TryRead(FMOD.System system, int index, out AudioDeviceInfo info)
        {
            FMOD.RESULT result = system.getDriverInfo(index, out string name, NameLength, out Guid guid, out int rate, out _, out _);
            if (result != FMOD.RESULT.OK)
            {
                info = default;
                return false;
            }
            info = new AudioDeviceInfo(guid, name, rate);
            return true;
        }
    }
}
