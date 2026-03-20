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
            _current = string.IsNullOrEmpty(json)
                ? new SettingsData()
                : JsonAdapter.FromJson<SettingsData>(json);
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

            // 고정 해상도 목록 (16:9) — GraphicSettingUI.Resolutions와 동기화 필요
            var resolutions = new (int w, int h)[]
            {
                (1024, 576), (1152, 648), (1280, 720),
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

            // Sound: IAudioManager에 볼륨 제어 메서드 추가 후 여기서 적용
            // TODO: ServiceLocator.TryGet<IAudioManager>(out var audio) → audio.SetVolume(...)

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
