using FMODUnity;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using UnityEngine;

namespace SCOdyssey.App
{
    // FMOD 초기화 전에 버퍼 크기를 PlayerPrefs에서 읽어 적용.
    // setDSPBufferSize는 FMOD system.init() 전에만 유효하므로
    // SubsystemRegistration(가장 이른 초기화 단계)에서 실행.
    internal static class FMODAudioPreInit
    {
        private static readonly int[] BufferSizes = { 64, 128, 256, 512, 1024 };

        // SettingsManager.PREFS_KEY와 동일하게 유지
        private const string PrefsKey = "SCOdyssey.Settings.v1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ApplyBufferSize()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;

            var data = JsonAdapter.FromJson<SettingsData>(json);
            if (data.audioBufferIndex < 0 || data.audioBufferIndex >= BufferSizes.Length) return;

            int bufferSize = BufferSizes[data.audioBufferIndex];
            var fmodSettings = Settings.Instance;

            // FindCurrentPlatform()이 internal이므로 모든 플랫폼에 일괄 적용
            // 체인 탐색 시 어느 플랫폼이 선택되더라도 버퍼 크기가 반영됨
            foreach (var platform in fmodSettings.Platforms)
                platform.SetDSPBufferLength(bufferSize);
            fmodSettings.DefaultPlatform.SetDSPBufferLength(bufferSize);
        }
    }
}
