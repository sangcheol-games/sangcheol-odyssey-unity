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
            // vSync가 켜져 있으면 targetFrameRate가 통째로 무시되고 모니터 주사율에 고정된다.
            // 입력 이벤트는 프레임당 한 번 flush되므로 프레임 간격이 곧 타격음 지연의 지터 폭이 된다.
            // 리듬게임 기본값대로 vSync를 끄고 targetFrameRate가 실제로 동작하게 한다.
            // TODO: vSync를 끄면 화면 티어링이 생길 수 있다. 감수할지는 유저가 고를 문제이므로
            //       SettingsData에 vSync 항목을 추가하고 그래픽 설정 UI에 토글로 노출할 것 (기본값 off).
            QualitySettings.vSyncCount = 0;
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
