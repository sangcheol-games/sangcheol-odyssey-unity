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

        public void Apply()
        {
            // Graphic
            Application.targetFrameRate = _current.targetFrameRate;

            var resolutions = Screen.resolutions;
            if (_current.resolutionIndex >= 0 && _current.resolutionIndex < resolutions.Length)
            {
                var res = resolutions[_current.resolutionIndex];
                Screen.SetResolution(res.width, res.height, _current.fullScreen);
            }
            else
            {
                Screen.fullScreen = _current.fullScreen;
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
