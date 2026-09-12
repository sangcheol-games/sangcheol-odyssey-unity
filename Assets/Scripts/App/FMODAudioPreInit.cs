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

        // RuntimeManager.Initialize()의 가드가 `DSPBufferLength > 0 && DSPBufferCount > 0`이라
        // Count를 함께 설정하지 않으면 setDSPBufferSize 호출 자체가 건너뛰어지고
        // FMOD 플랫폼 기본값(Windows 통상 1024 x 4)이 그대로 쓰인다.
        // 4는 FMOD 권장 기본값. 낮추면 지연은 줄지만 언더런(찍찍거림) 위험이 커진다.
        private const int DspBufferCount = 4;

        // SettingsManager.PREFS_KEY와 동일하게 유지
        private const string PrefsKey = "SCOdyssey.Settings.v1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ApplyBufferSize()
        {
            // 저장된 설정이 없거나 깨졌으면 SettingsData의 기본값(audioBufferIndex = 2 → 256)을 쓴다.
            // 첫 실행에도 버퍼가 반드시 적용되어야 하므로 조기 return하지 않는다.
            // (return하면 FMOD 플랫폼 기본값이 그대로 쓰여 지연이 크게 늘어난다)
            var data = ReadSettings();

            int index = data.audioBufferIndex;
            if (index < 0 || index >= BufferSizes.Length) index = new SettingsData().audioBufferIndex;

            int bufferSize = BufferSizes[index];
            var fmodSettings = Settings.Instance;

            // FindCurrentPlatform()이 internal이므로 모든 플랫폼에 일괄 적용
            // 체인 탐색 시 어느 플랫폼이 선택되더라도 버퍼 크기가 반영됨
            //
            // Length와 Count를 반드시 짝으로 설정한다 — RuntimeManager.Initialize()가
            // `DSPBufferLength > 0 && DSPBufferCount > 0`일 때만 setDSPBufferSize를 호출하므로
            // 한쪽만 설정하면 버퍼 설정 전체가 조용히 무시된다.
            foreach (var platform in fmodSettings.Platforms)
            {
                platform.SetDSPBufferLength(bufferSize);
                platform.SetDSPBufferCount(DspBufferCount);
            }
            fmodSettings.DefaultPlatform.SetDSPBufferLength(bufferSize);
            fmodSettings.DefaultPlatform.SetDSPBufferCount(DspBufferCount);
        }

        // PlayerPrefs의 설정 JSON을 읽는다. 없거나 파싱에 실패하면 기본값 인스턴스를 반환.
        private static SettingsData ReadSettings()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return new SettingsData();

            try
            {
                return JsonAdapter.FromJson<SettingsData>(json) ?? new SettingsData();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FMODAudioPreInit] 설정 파싱 실패, 기본 버퍼 크기를 사용합니다: {e.Message}");
                return new SettingsData();
            }
        }
    }
}
