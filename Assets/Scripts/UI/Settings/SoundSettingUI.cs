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
            Btn_Save,
            Btn_Reset,
            Btn_Close
        }

        private enum Texts
        {
            Text_MasterVolumeValue,
            Text_BgmVolumeValue,
            Text_HitSoundVolumeValue,
            Text_SfxVolumeValue,
            Text_AudioDeviceValue
        }

        private enum Sliders
        {
            Slider_MasterVolume,
            Slider_BgmVolume,
            Slider_HitSoundVolume,
            Slider_SfxVolume
        }

        // _pending: UI에서 변경한 값을 임시로 보관. Btn_Save를 눌러야 실제로 저장됨.
        private SettingsData _pending;
        private string[] _audioDevices;
        private int _audioDeviceIndex;

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
            _audioDevices = ServiceLocator.Get<IAudioManager>().GetAvailableDevices();
            if (_audioDevices.Length == 0) _audioDevices = new[] { "기본 장치" };
            _audioDeviceIndex = Mathf.Clamp(_pending.audioDeviceIndex, 0, _audioDevices.Length - 1);
            RefreshAudioDeviceText();

            GetButton((int)Buttons.Btn_AudioDevicePrev).onClick.AddListener(() =>
            {
                if (_audioDeviceIndex <= 0) return;
                _pending.audioDeviceIndex = --_audioDeviceIndex;
                RefreshAudioDeviceText();
            });
            GetButton((int)Buttons.Btn_AudioDeviceNext).onClick.AddListener(() =>
            {
                if (_audioDeviceIndex >= _audioDevices.Length - 1) return;
                _pending.audioDeviceIndex = ++_audioDeviceIndex;
                RefreshAudioDeviceText();
            });
            #endregion

            #region Volume
            InitVolumeSlider(Sliders.Slider_MasterVolume,   Texts.Text_MasterVolumeValue,   _pending.masterVolume,   v => _pending.masterVolume   = v);
            InitVolumeSlider(Sliders.Slider_BgmVolume,      Texts.Text_BgmVolumeValue,      _pending.bgmVolume,      v => _pending.bgmVolume      = v);
            InitVolumeSlider(Sliders.Slider_HitSoundVolume, Texts.Text_HitSoundVolumeValue, _pending.hitSoundVolume, v => _pending.hitSoundVolume = v);
            InitVolumeSlider(Sliders.Slider_SfxVolume,      Texts.Text_SfxVolumeValue,      _pending.sfxVolume,      v => _pending.sfxVolume      = v);
            #endregion

            GetButton((int)Buttons.Tab_Game).onClick.AddListener(SwitchToGame);
            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Account).onClick.AddListener(SwitchToAccount);

            GetButton((int)Buttons.Btn_Save)?.onClick.AddListener(OnClickSave);
            GetButton((int)Buttons.Btn_Reset)?.onClick.AddListener(OnClickReset);
            GetButton((int)Buttons.Btn_Close).onClick.AddListener(OnClickClose);
        }

        #region Audio Device

        private void RefreshAudioDeviceText()
        {
            GetText((int)Texts.Text_AudioDeviceValue).text = _audioDevices[_audioDeviceIndex];
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
            settings.Current.audioDeviceIndex = _pending.audioDeviceIndex;
            if (ServiceLocator.TryGet<IAudioManager>(out var audio))
                audio.SetAudioDevice(_pending.audioDeviceIndex);
            settings.Current.masterVolume   = _pending.masterVolume;
            settings.Current.bgmVolume      = _pending.bgmVolume;
            settings.Current.hitSoundVolume = _pending.hitSoundVolume;
            settings.Current.sfxVolume      = _pending.sfxVolume;
            settings.Apply();
            settings.Save();
        }

        private void OnClickReset()
        {
            // _pending만 기본값으로 갱신 — Save를 눌러야 실제로 적용됨
            _pending = new SettingsData();
            _audioDeviceIndex = _pending.audioDeviceIndex;
            RefreshAudioDeviceText();
            Get<Slider>((int)Sliders.Slider_MasterVolume).value   = _pending.masterVolume;
            Get<Slider>((int)Sliders.Slider_BgmVolume).value      = _pending.bgmVolume;
            Get<Slider>((int)Sliders.Slider_HitSoundVolume).value = _pending.hitSoundVolume;
            Get<Slider>((int)Sliders.Slider_SfxVolume).value      = _pending.sfxVolume;
        }

        private void OnClickClose()
        {
            // _pending은 버려지고 UI만 닫힘 — 저장된 설정값은 변경되지 않음
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        private void SwitchToGame()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<GameSettingUI>();
        }

        private void SwitchToGraphic()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<GraphicSettingUI>();
        }

        private void SwitchToAccount()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<AccountSettingUI>();
        }

        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() => OnClickSave();
        protected override void HandleCancel() => OnClickClose();
    }
}
