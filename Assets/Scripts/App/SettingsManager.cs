using System;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using UnityEngine;

namespace SCOdyssey.App
{
    public class SettingsManager : ISettingsManager
    {
        private const string PREFS_KEY = "SCOdyssey.Settings.v1";

        private SettingsData _current;

        public SettingsData Current => _current;

        public event Action<SettingsData> OnSettingsChanged;


        public void Load()
        {
            var json = PlayerPrefs.GetString(PREFS_KEY, "");
            if (string.IsNullOrEmpty(json))
            {
                _current = new SettingsData();
                return;
            }
            _current = JsonAdapter.FromJson<SettingsData>(json);
            MigrateLegacy(_current);
        }

        // audioOutputType 필드가 없던 구버전 저장본 호환.
        // 그 시절 audioDeviceIndex는 FMOD AUTODETECT(=Windows에서 WASAPI) 기준의 드라이버 인덱스였음.
        // FMODAudioPreInit.EnumerateAllDevices는 AUTODETECT를 enumerate하지 않으므로 0으로 두면
        // 설정 UI의 FindDeviceListIndex가 매칭에 실패해 첫 항목으로 폴백 → 사용자 선택이 사라짐.
        // Windows에서 AUTODETECT ≡ WASAPI이므로 1로 정규화해 (1, audioDeviceIndex) 매칭을 보존.
        private static void MigrateLegacy(SettingsData data)
        {
            if (data.audioOutputType == 0) data.audioOutputType = 1;
        }

        public void Save()
        {
            PlayerPrefs.SetString(PREFS_KEY, JsonAdapter.ToJson(_current));
            PlayerPrefs.Save();
        }

        private static readonly FullScreenMode[] DisplayModes =
        {
            FullScreenMode.ExclusiveFullScreen, // 0: 전체 화면
            FullScreenMode.Windowed,            // 1: 창 모드
            FullScreenMode.FullScreenWindow,    // 2: 전체 창 모드
        };

        public void Apply()
        {
            // Graphic
            Application.targetFrameRate = _current.targetFrameRate;

            var mode = (_current.displayMode >= 0 && _current.displayMode < DisplayModes.Length)
                ? DisplayModes[_current.displayMode]
                : FullScreenMode.ExclusiveFullScreen;

            // 고정 해상도 목록 — GraphicSettingUI.Resolutions와 동기화 필요
            var resolutions = new (int w, int h)[]
            {
                (1024, 768), (1280, 720),
                (1366, 768), (1600, 900), (1920, 1080)
            };
            if (_current.resolutionIndex >= 0 && _current.resolutionIndex < resolutions.Length)
            {
                var res = resolutions[_current.resolutionIndex];
                Screen.SetResolution(res.w, res.h, mode);
            }
            else
            {
                Screen.fullScreenMode = mode;
            }

            // Sound
            if (ServiceLocator.TryGet<IAudioManager>(out var audio))
            {
                audio.SetMasterVolume(_current.masterVolume);
                audio.SetBgmVolume(_current.bgmVolume);
                audio.SetHitSoundVolume(_current.hitSoundVolume);
                audio.SetSfxVolume(_current.sfxVolume);
            }

            // Input
            UnityEngine.InputSystem.InputSystem.pollingFrequency = _current.inputPollingRateHz;

            OnSettingsChanged?.Invoke(_current);
        }

        public void ResetToDefault()
        {
            _current = new SettingsData();
            Apply();
            Save();
        }
    }
}
