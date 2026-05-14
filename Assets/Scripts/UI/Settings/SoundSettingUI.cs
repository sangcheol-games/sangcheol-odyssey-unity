using System.Collections.Generic;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey
{
    public class SoundSettingUI : BaseUI
    {
        private enum Buttons
        {
            Tab_Game,
            Tab_Graphic,
            Tab_Sound,
            Tab_Account,
            Btn_AudioDevicePrev,
            Btn_AudioDeviceNext,
            Btn_PlayInBackgroundPrev,
            Btn_PlayInBackgroundNext,
            Btn_Reset,
            Btn_Close
        }

        private enum Texts
        {
            Text_MasterVolumeValue,
            Text_BgmVolumeValue,
            Text_HitSoundVolumeValue,
            Text_SfxVolumeValue,
            Text_AudioDeviceValue,
            Text_PlayInBackgroundValue,
            Text_BufferSizeValue
        }

        private enum Sliders
        {
            Slider_MasterVolume,
            Slider_BgmVolume,
            Slider_HitSoundVolume,
            Slider_SfxVolume,
            Slider_BufferSize
        }

        private static readonly string[] PlayInBackgroundLabels = { "OFF", "ON" };

        // _pending: UI에서 변경한 값을 임시로 보관. Btn_Save를 눌러야 실제로 저장됨.
        private SettingsData _pending;
        // 모든 출력 모드 통합 디바이스 목록 — FMODAudioPreInit이 부트 시 채워둠
        private List<AudioDeviceEntry> _allDevices;
        private int _audioDeviceListIndex;

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            Bind<TMPro.TMP_Text>(typeof(Texts));
            Bind<Slider>(typeof(Sliders));

            // 현재 저장된 설정을 깊은 복사 → _pending에 저장
            var current = ServiceLocator.Get<ISettingsManager>().Current;
            _pending = JsonAdapter.FromJson<SettingsData>(JsonAdapter.ToJson(current));

            #region Audio Device
            // FMODAudioPreInit.AllDevices(부트 시 모드별로 모은 통합 목록)을 그대로 사용
            _allDevices = new List<AudioDeviceEntry>(FMODAudioPreInit.AllDevices);
            if (_allDevices.Count == 0)
            {
                // 폴백 — enumeration 자체가 실패한 환경
                _allDevices.Add(new AudioDeviceEntry
                {
                    OutputType = 0,
                    DriverIndex = 0,
                    DisplayName = "기본 장치"
                });
            }
            _audioDeviceListIndex = FindDeviceListIndex(_pending.audioOutputType, _pending.audioDeviceIndex);
            RefreshAudioDeviceText();

            GetButton((int)Buttons.Btn_AudioDevicePrev).onClick.AddListener(() =>
            {
                if (_audioDeviceListIndex <= 0) return;
                _audioDeviceListIndex--;
                ApplySelectedDevice();
            });
            GetButton((int)Buttons.Btn_AudioDeviceNext).onClick.AddListener(() =>
            {
                if (_audioDeviceListIndex >= _allDevices.Count - 1) return;
                _audioDeviceListIndex++;
                ApplySelectedDevice();
            });
            #endregion

            #region Volume
            InitVolumeSlider(Sliders.Slider_MasterVolume,   Texts.Text_MasterVolumeValue,   _pending.masterVolume,   v => _pending.masterVolume   = v);
            InitVolumeSlider(Sliders.Slider_BgmVolume,      Texts.Text_BgmVolumeValue,      _pending.bgmVolume,      v => _pending.bgmVolume      = v);
            InitVolumeSlider(Sliders.Slider_HitSoundVolume, Texts.Text_HitSoundVolumeValue, _pending.hitSoundVolume, v => _pending.hitSoundVolume = v);
            InitVolumeSlider(Sliders.Slider_SfxVolume,      Texts.Text_SfxVolumeValue,      _pending.sfxVolume,      v => _pending.sfxVolume      = v);
            #endregion

            #region Play In Background
            GetText((int)Texts.Text_PlayInBackgroundValue).text = PlayInBackgroundLabels[_pending.playInBackground ? 1 : 0];
            GetButton((int)Buttons.Btn_PlayInBackgroundPrev).onClick.AddListener(() =>
            {
                if (!_pending.playInBackground) return;
                _pending.playInBackground = false;
                GetText((int)Texts.Text_PlayInBackgroundValue).text = PlayInBackgroundLabels[0];
            });
            GetButton((int)Buttons.Btn_PlayInBackgroundNext).onClick.AddListener(() =>
            {
                if (_pending.playInBackground) return;
                _pending.playInBackground = true;
                GetText((int)Texts.Text_PlayInBackgroundValue).text = PlayInBackgroundLabels[1];
            });
            #endregion

            #region Buffer Size
            var bufferSizes = new[] { 64, 128, 256, 512, 1024 };
            var bufferSlider = Get<Slider>((int)Sliders.Slider_BufferSize);
            bufferSlider.wholeNumbers = true;
            bufferSlider.minValue = 0;
            bufferSlider.maxValue = 4;
            bufferSlider.value = _pending.audioBufferIndex;
            GetText((int)Texts.Text_BufferSizeValue).text = bufferSizes[_pending.audioBufferIndex].ToString();
            bufferSlider.onValueChanged.AddListener(v =>
            {
                int idx = Mathf.RoundToInt(v);
                _pending.audioBufferIndex = idx;
                GetText((int)Texts.Text_BufferSizeValue).text = bufferSizes[idx].ToString();
            });
            #endregion

            GetButton((int)Buttons.Tab_Game).onClick.AddListener(SwitchToGame);
            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Account).onClick.AddListener(SwitchToAccount);

            GetButton((int)Buttons.Btn_Reset)?.onClick.AddListener(OnClickReset);
            GetButton((int)Buttons.Btn_Close).onClick.AddListener(OnClickClose);
        }

        #region Audio Device

        private void RefreshAudioDeviceText()
        {
            GetText((int)Texts.Text_AudioDeviceValue).text = _allDevices[_audioDeviceListIndex].DisplayName;
        }

        // (outputType, driverIndex) 조합과 일치하는 통합 목록 인덱스를 찾음. 없으면 0.
        private int FindDeviceListIndex(int outputType, int driverIndex)
        {
            for (int i = 0; i < _allDevices.Count; i++)
            {
                var e = _allDevices[i];
                if (e.OutputType == outputType && e.DriverIndex == driverIndex) return i;
            }
            return 0;
        }

        // 현재 선택된 항목을 _pending에 반영하고 텍스트 갱신.
        private void ApplySelectedDevice()
        {
            var entry = _allDevices[_audioDeviceListIndex];
            _pending.audioOutputType = entry.OutputType;
            _pending.audioDeviceIndex = entry.DriverIndex;
            RefreshAudioDeviceText();
        }

        #endregion

        #region Volume

        private void InitVolumeSlider(Sliders sliderEnum, Texts textEnum, float initialValue, System.Action<float> onChanged)
        {
            var slider = Get<Slider>((int)sliderEnum);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = initialValue;
            GetText((int)textEnum).text = ToPercent(initialValue);
            slider.onValueChanged.AddListener(v =>
            {
                onChanged(v);
                GetText((int)textEnum).text = ToPercent(v);
            });
        }

        private string ToPercent(float value) => $"{Mathf.RoundToInt(value * 100)}%";

        #endregion

        private void OnClickSave()
        {
            // _pending의 값을 Current에 복사한 뒤 Apply(시스템 반영) + Save(PlayerPrefs 저장)
            var settings = ServiceLocator.Get<ISettingsManager>();
            bool outputTypeChanged = settings.Current.audioOutputType != _pending.audioOutputType;
            settings.Current.audioOutputType     = _pending.audioOutputType;
            settings.Current.audioDeviceIndex    = _pending.audioDeviceIndex;
            settings.Current.playInBackground     = _pending.playInBackground;
            // 출력 모드(ASIO/WASAPI/...)가 바뀌면 런타임 setDriver 불가 → 다음 시작 시 적용.
            // 같은 모드 내 디바이스 변경만 즉시 반영.
            if (!outputTypeChanged && ServiceLocator.TryGet<IAudioManager>(out var audio))
                audio.SetAudioDevice(_pending.audioDeviceIndex);
            settings.Current.masterVolume   = _pending.masterVolume;
            settings.Current.bgmVolume      = _pending.bgmVolume;
            settings.Current.hitSoundVolume = _pending.hitSoundVolume;
            settings.Current.sfxVolume       = _pending.sfxVolume;
            // 버퍼 크기/출력 타입은 FMODAudioPreInit에서 다음 시작 시 적용됨 (런타임 변경 불가)
            settings.Current.audioBufferIndex = _pending.audioBufferIndex;
            settings.Apply();
            settings.Save();
        }

        private void OnClickReset()
        {
            // _pending만 기본값으로 갱신 — Save를 눌러야 실제로 적용됨
            _pending = new SettingsData();
            _audioDeviceListIndex = FindDeviceListIndex(_pending.audioOutputType, _pending.audioDeviceIndex);
            RefreshAudioDeviceText();
            GetText((int)Texts.Text_PlayInBackgroundValue).text = PlayInBackgroundLabels[0]; // "OFF"
            Get<Slider>((int)Sliders.Slider_MasterVolume).value   = _pending.masterVolume;
            Get<Slider>((int)Sliders.Slider_BgmVolume).value      = _pending.bgmVolume;
            Get<Slider>((int)Sliders.Slider_HitSoundVolume).value = _pending.hitSoundVolume;
            Get<Slider>((int)Sliders.Slider_SfxVolume).value       = _pending.sfxVolume;
            Get<Slider>((int)Sliders.Slider_BufferSize).value      = _pending.audioBufferIndex; // = 2 (256)
        }

        private void OnClickClose()
        {
            // _pending은 버려지고 UI만 닫힘 — 저장된 설정값은 변경되지 않음
            var audioManager = ServiceLocator.Get<IAudioManager>();
            audioManager.PlaySound("ui_button_simple_click_06.wav");
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        private void SwitchToGame()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.SwapUI<GameSettingUI>();
        }

        private void SwitchToGraphic()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.SwapUI<GraphicSettingUI>();
        }

        private void SwitchToAccount()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.SwapUI<AccountSettingUI>();
        }

        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() => OnClickSave();
        protected override void HandleCancel() => OnClickClose();
    }
}
