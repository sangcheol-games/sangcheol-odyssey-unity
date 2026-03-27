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
            settings.Current.audioDeviceIndex    = _pending.audioDeviceIndex;
            settings.Current.playInBackground     = _pending.playInBackground;
            if (ServiceLocator.TryGet<IAudioManager>(out var audio))
                audio.SetAudioDevice(_pending.audioDeviceIndex);
            settings.Current.masterVolume   = _pending.masterVolume;
            settings.Current.bgmVolume      = _pending.bgmVolume;
            settings.Current.hitSoundVolume = _pending.hitSoundVolume;
            settings.Current.sfxVolume       = _pending.sfxVolume;
            // 버퍼 크기는 FMODAudioPreInit에서 다음 시작 시 적용됨 (런타임 변경 불가)
            settings.Current.audioBufferIndex = _pending.audioBufferIndex;
            settings.Apply();
            settings.Save();
        }

        private void OnClickReset()
        {
            // _pending만 기본값으로 갱신 — Save를 눌러야 실제로 적용됨
            _pending = new SettingsData();
            _audioDeviceIndex = _pending.audioDeviceIndex;
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
