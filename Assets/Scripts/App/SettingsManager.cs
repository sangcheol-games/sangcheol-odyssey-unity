using System;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using UnityEngine;

namespace SCOdyssey.App
{
    public class SettingsManager : ISettingsManager
    {
        // 키는 v1 이름 그대로 쓴다(내용은 v2). 마이그레이션과 백업 규칙은 SettingsMigration.
        private const string PREFS_KEY = SettingsMigration.PrefsKey;

        private SettingsData _current;

        public SettingsData Current => _current;

        public event Action<SettingsData> OnSettingsChanged;


        public void Load()
        {
            var json = PlayerPrefs.GetString(PREFS_KEY, "");
            _current = SettingsMigration.Parse(json, out SettingsLoadOutcome outcome);

            if (outcome == SettingsLoadOutcome.Corrupt)
            {
                // 손상된 원문은 남겨 두고 기본값으로 덮어쓴다.
                PlayerPrefs.SetString(SettingsMigration.CorruptBackupKey, json);
                Debug.LogWarning("[Settings] 저장된 설정이 손상되어 기본값으로 시작합니다. 원문: " + SettingsMigration.CorruptBackupKey);
                Save();
            }
            else if (outcome == SettingsLoadOutcome.MigratedFromV1)
            {
                // v1 원문은 처음 한 번만 백업한다(되돌릴 때 이 값을 원래 키에 넣는다).
                if (!PlayerPrefs.HasKey(SettingsMigration.V1BackupKey)) PlayerPrefs.SetString(SettingsMigration.V1BackupKey, json);
                Debug.Log("[Settings] 설정을 v1에서 v2로 옮겼습니다. 원문: " + SettingsMigration.V1BackupKey);
                Save();
            }
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

            // Sound: 볼륨은 믹서에 바로 넣는다. 출력(타입, 장치, 버퍼)은 여기서 적용하지 않는다(부팅은 Installer, 설정 화면은 ApplyAsync).
            if (ServiceLocator.TryGet<IAudioMixer>(out var mixer))
            {
                mixer.Master.Volume = _current.masterVolume;
                mixer.Music.Volume = _current.bgmVolume;
                mixer.HitSound.Volume = _current.hitSoundVolume;
                mixer.Sfx.Volume = _current.sfxVolume;
            }
            // 로비 음소거는 오디오 모듈이 포커스가 바뀔 때마다 playInBackground를 읽어 처리한다.
            Application.runInBackground = _current.playInBackground;

            // Input
            UnityEngine.InputSystem.InputSystem.pollingFrequency = _current.inputPollingRateHz;

            OnSettingsChanged?.Invoke(_current);
        }

        // 출력 설정도 기본값(WASAPI, 기본 장치, 256x4)으로 돌아가며 다음 부팅부터 적용된다.
        public void ResetToDefault()
        {
            _current = new SettingsData();
            Apply();
            Save();
        }
    }
}
