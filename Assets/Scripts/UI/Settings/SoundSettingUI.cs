using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey
{
    // 사운드 설정 화면. 볼륨·백그라운드 재생은 Save 때 바로 저장하고,
    // 출력(장치, 버퍼)은 IAudioOutputService.ApplyAsync(close→init)로 적용한 뒤 요청 구성으로 성공했을 때만 저장한다(결과는 로그).
    // 장치 목록은 하나다: 기본 장치 → WASAPI 장치 → ASIO 드라이버(지원할 때만). 장치를 고르면 출력 타입도 정해진다.
    // 목록은 비동기로 읽고, GUID로 저장하므로 목록 순서가 바뀌어도 같은 장치를 가리킨다.
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

        // 통합 장치 목록의 한 항목. 이름은 구분 표시 없이 그대로 보여 준다.
        private readonly struct DeviceEntry
        {
            public readonly AudioOutputKind Kind;
            public readonly AudioDeviceInfo Device;

            public DeviceEntry(AudioOutputKind kind, AudioDeviceInfo device)
            {
                Kind = kind;
                Device = device;
            }
        }

        private const string PlayInBackgroundOff = "OFF";
        private const string PlayInBackgroundOn = "ON";

        // _pending: UI에서 변경한 값을 임시로 보관. Btn_Save를 눌러야 실제로 저장됨. 출력 값도 v2 필드에 담는다.
        private SettingsData _pending;
        private IAudioOutputService _output;
        private readonly List<DeviceEntry> _devices = new List<DeviceEntry>();
        private bool _loadingDevices;
        private int _deviceRequest;
        private bool _applying;
        private bool _updatingBufferSlider;
        private bool _initialized;

        private void Start()
        {
            Init();
        }

        // 일회성 배선만 담당 (Bind/AddListener는 1회면 충분)
        private void Init()
        {
            BindButton(typeof(Buttons));
            Bind<TMPro.TMP_Text>(typeof(Texts));
            Bind<Slider>(typeof(Sliders));

            ServiceLocator.TryGet(out _output);

            #region Audio Device
            GetButton((int)Buttons.Btn_AudioDevicePrev).onClick.AddListener(() => ChangeDevice(-1));
            GetButton((int)Buttons.Btn_AudioDeviceNext).onClick.AddListener(() => ChangeDevice(1));
            #endregion

            #region Volume
            WireVolumeSlider(Sliders.Slider_MasterVolume,   Texts.Text_MasterVolumeValue,   v => _pending.masterVolume   = v);
            WireVolumeSlider(Sliders.Slider_BgmVolume,      Texts.Text_BgmVolumeValue,      v => _pending.bgmVolume      = v);
            WireVolumeSlider(Sliders.Slider_HitSoundVolume, Texts.Text_HitSoundVolumeValue, v => _pending.hitSoundVolume = v);
            WireVolumeSlider(Sliders.Slider_SfxVolume,      Texts.Text_SfxVolumeValue,      v => _pending.sfxVolume      = v);
            #endregion

            #region Play In Background
            GetButton((int)Buttons.Btn_PlayInBackgroundPrev).onClick.AddListener(() =>
            {
                if (!_pending.playInBackground) return;
                _pending.playInBackground = false;
                RefreshPlayInBackgroundText();
            });
            GetButton((int)Buttons.Btn_PlayInBackgroundNext).onClick.AddListener(() =>
            {
                if (_pending.playInBackground) return;
                _pending.playInBackground = true;
                RefreshPlayInBackgroundText();
            });
            #endregion

            #region Buffer Size
            var bufferSlider = Get<Slider>((int)Sliders.Slider_BufferSize);
            bufferSlider.wholeNumbers = true;
            bufferSlider.minValue = 0;
            bufferSlider.onValueChanged.AddListener(v =>
            {
                if (_updatingBufferSlider) return;
                int[] presets = AudioSettingsMapper.BufferPresets;
                int index = Mathf.Clamp(Mathf.RoundToInt(v), 0, presets.Length - 1);
                _pending.dspBufferLength = presets[index];
                RefreshBufferText();
            });
            #endregion

            GetButton((int)Buttons.Tab_Game).onClick.AddListener(SwitchToGame);
            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Account).onClick.AddListener(SwitchToAccount);

            GetButton((int)Buttons.Btn_Save)?.onClick.AddListener(OnClickSave);
            GetButton((int)Buttons.Btn_Reset)?.onClick.AddListener(OnClickReset);
            GetButton((int)Buttons.Btn_Close).onClick.AddListener(OnClickClose);

            RefreshFromSettings();   // 최초 1회 채우기
            _initialized = true;
        }

        // 재진입(재활성화)마다 저장된 설정값을 다시 로드해 stale 방지
        protected override void OnEnable()
        {
            base.OnEnable();
            if (_initialized) RefreshFromSettings();
        }

        private AudioOutputKind PendingKind
        {
            get { return AudioSettingsMapper.ToOutputKind(_pending.audioOutputType); }
        }

        // 저장된 설정을 _pending에 깊은 복사로 로드한 뒤 UI에 반영. 장치 목록은 새로 읽는다.
        private void RefreshFromSettings()
        {
            var current = ServiceLocator.Get<ISettingsManager>().Current;
            _pending = JsonAdapter.FromJson<SettingsData>(JsonAdapter.ToJson(current));
            ApplyPendingToUI(true);
        }

        // 현재 _pending 값을 모든 UI 컴포넌트에 반영 (RefreshFromSettings / OnClickReset 공용)
        private void ApplyPendingToUI(bool refreshDevices)
        {
            SetVolumeSlider(Sliders.Slider_MasterVolume,   Texts.Text_MasterVolumeValue,   _pending.masterVolume);
            SetVolumeSlider(Sliders.Slider_BgmVolume,      Texts.Text_BgmVolumeValue,      _pending.bgmVolume);
            SetVolumeSlider(Sliders.Slider_HitSoundVolume, Texts.Text_HitSoundVolumeValue, _pending.hitSoundVolume);
            SetVolumeSlider(Sliders.Slider_SfxVolume,      Texts.Text_SfxVolumeValue,      _pending.sfxVolume);

            RefreshPlayInBackgroundText();
            RefreshBufferSlider();
            LoadDevices(refreshDevices).Forget();
        }

        private void RefreshPlayInBackgroundText()
        {
            string label = PlayInBackgroundOff;
            if (_pending.playInBackground) label = PlayInBackgroundOn;
            GetText((int)Texts.Text_PlayInBackgroundValue).text = label;
        }

        #region Audio Device

        private async UniTaskVoid LoadDevices(bool refresh)
        {
            int request = ++_deviceRequest;
            if (_output == null)
            {
                _devices.Clear();
                RefreshAudioDeviceText();
                return;
            }

            _loadingDevices = true;
            RefreshAudioDeviceText();
            IReadOnlyList<AudioDeviceInfo> wasapi;
            IReadOnlyList<AudioDeviceInfo> asio = null;
            try
            {
                CancellationToken ct = this.GetCancellationTokenOnDestroy();
                wasapi = await _output.GetDevicesAsync(AudioOutputKind.Wasapi, refresh, ct);
                if (_output.IsSupported(AudioOutputKind.Asio)) asio = await _output.GetDevicesAsync(AudioOutputKind.Asio, refresh, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            // 기다리는 동안 다시 불렸으면 이 결과는 버린다.
            if (request != _deviceRequest) return;

            _devices.Clear();
            AddDevices(AudioOutputKind.Wasapi, wasapi);
            if (asio != null) AddDevices(AudioOutputKind.Asio, asio);
            _loadingDevices = false;
            RefreshAudioDeviceText();
        }

        private void AddDevices(AudioOutputKind kind, IReadOnlyList<AudioDeviceInfo> devices)
        {
            for (int i = 0; i < devices.Count; i++) _devices.Add(new DeviceEntry(kind, devices[i]));
        }

        // 위치 -1 = 기본 장치(WASAPI 기본 장치 따라가기), 0.. = 목록 번호. 저장된 장치가 목록에 없으면 -2.
        // ASIO + GUID 빈 값(첫 ASIO 드라이버)으로 저장돼 있으면 목록의 첫 ASIO 드라이버로 본다.
        private int PendingDevicePosition()
        {
            AudioOutputKind kind = PendingKind;
            Guid id = AudioSettingsMapper.ParseDeviceGuid(_pending.deviceGuid);
            if (id == Guid.Empty && kind == AudioOutputKind.Wasapi) return -1;
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].Kind != kind) continue;
                if (id == Guid.Empty || _devices[i].Device.Id == id) return i;
            }
            return -2;
        }

        private void ChangeDevice(int delta)
        {
            if (_loadingDevices) return;
            int position = PendingDevicePosition();
            if (position == -2) position = -1;
            int next = position + delta;
            if (next < -1 || next >= _devices.Count) return;

            AudioOutputKind previousKind = PendingKind;
            if (next == -1)
            {
                _pending.audioOutputType = AudioSettingsMapper.ToOutputType(AudioOutputKind.Wasapi);
                _pending.deviceGuid = "";
                _pending.deviceName = "";
            }
            else
            {
                DeviceEntry entry = _devices[next];
                _pending.audioOutputType = AudioSettingsMapper.ToOutputType(entry.Kind);
                _pending.deviceGuid = entry.Device.Id.ToString();
                _pending.deviceName = entry.Device.Name;
            }

            // 출력 타입이 바뀌면 버퍼 개수만 그 타입 기본값(WASAPI 4, ASIO 2)으로 둔다. 길이는 고른 값 그대로다.
            AudioOutputKind kind = PendingKind;
            if (kind != previousKind) _pending.dspBufferCount = AudioSettingsMapper.DefaultBufferCount(kind);
            RefreshAudioDeviceText();
        }

        private void RefreshAudioDeviceText()
        {
            if (_loadingDevices)
            {
                SetText(Texts.Text_AudioDeviceValue, AudioUiText.Searching);
                return;
            }

            int position = PendingDevicePosition();
            if (position == -1) SetText(Texts.Text_AudioDeviceValue, AudioUiText.DefaultDevice);
            else if (position == -2) SetText(Texts.Text_AudioDeviceValue, _pending.deviceName + AudioUiText.DeviceMissing);
            else SetText(Texts.Text_AudioDeviceValue, _devices[position].Device.Name);
        }

        #endregion

        #region Buffer Size

        private void RefreshBufferSlider()
        {
            int[] presets = AudioSettingsMapper.BufferPresets;
            _pending.dspBufferLength = AudioSettingsMapper.NearestPreset(_pending.dspBufferLength);
            var slider = Get<Slider>((int)Sliders.Slider_BufferSize);
            _updatingBufferSlider = true;
            slider.maxValue = presets.Length - 1;
            slider.value = AudioSettingsMapper.NearestIndex(presets, _pending.dspBufferLength);
            _updatingBufferSlider = false;
            RefreshBufferText();
        }

        private void RefreshBufferText()
        {
            GetText((int)Texts.Text_BufferSizeValue).text = _pending.dspBufferLength.ToString();
        }

        #endregion

        #region Volume

        private void WireVolumeSlider(Sliders sliderEnum, Texts textEnum, System.Action<float> onChanged)
        {
            var slider = Get<Slider>((int)sliderEnum);
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(v =>
            {
                onChanged(v);
                GetText((int)textEnum).text = ToPercent(v);
            });
        }

        private void SetVolumeSlider(Sliders sliderEnum, Texts textEnum, float value)
        {
            Get<Slider>((int)sliderEnum).value = value;
            GetText((int)textEnum).text = ToPercent(value);
        }

        private string ToPercent(float value) => $"{Mathf.RoundToInt(value * 100)}%";

        #endregion

        private void OnClickSave()
        {
            SaveAsync().Forget();
        }

        // 볼륨·백그라운드 재생은 곧바로 저장하고, 출력은 적용 결과를 본 뒤 저장한다. 적용 중에는 다시 누르지 못한다.
        private async UniTaskVoid SaveAsync()
        {
            if (_applying) return;
            _applying = true;
            SetSaveInteractable(false);

            var settings = ServiceLocator.Get<ISettingsManager>();
            settings.Current.playInBackground = _pending.playInBackground;
            settings.Current.masterVolume     = _pending.masterVolume;
            settings.Current.bgmVolume        = _pending.bgmVolume;
            settings.Current.hitSoundVolume   = _pending.hitSoundVolume;
            settings.Current.sfxVolume        = _pending.sfxVolume;
            settings.Apply();
            settings.Save();

            if (_output != null)
            {
                AudioOutputRequest request = AudioSettingsMapper.ToBootRequest(_pending);
                AudioApplyResult result = await _output.ApplyAsync(request, CancellationToken.None);
                if (this == null) return;
                HandleApplyResult(settings, result);
            }

            _applying = false;
            SetSaveInteractable(true);
        }

        private void HandleApplyResult(ISettingsManager settings, AudioApplyResult result)
        {
            AudioApplyOutcome outcome = result.Outcome;
            if (outcome == AudioApplyOutcome.Applied || outcome == AudioApplyOutcome.Unchanged)
            {
                // 요청 구성으로 동작 중일 때만 출력 설정을 저장한다(적용 중 크래시가 나도 다음 부팅은 이전 구성으로 연다).
                settings.Current.audioOutputType = _pending.audioOutputType;
                settings.Current.deviceGuid      = _pending.deviceGuid;
                settings.Current.deviceName      = _pending.deviceName;
                settings.Current.systemRate      = result.Actual.SampleRate;
                settings.Current.dspBufferLength = _pending.dspBufferLength;
                settings.Current.dspBufferCount  = _pending.dspBufferCount;
                settings.Save();
                Debug.Log("[SoundSettingUI] 출력 적용 " + outcome + ": " + result.Message);
                return;
            }
            // 폴백·Busy·Rejected·Failed는 저장하지 않는다.
            Debug.LogWarning("[SoundSettingUI] 출력 설정을 저장하지 않았습니다(" + outcome + "): " + result.Message);
        }

        private void SetSaveInteractable(bool interactable)
        {
            Button save = GetButton((int)Buttons.Btn_Save);
            if (save != null) save.interactable = interactable;
        }

        // 프리팹에 새 텍스트가 아직 없으면(Bind 실패) 건너뛴다.
        private void SetText(Texts textEnum, string value)
        {
            TMPro.TMP_Text text = GetText((int)textEnum);
            if (text != null) text.text = value;
        }

        private void OnClickReset()
        {
            // _pending만 기본값으로 갱신 — Save를 눌러야 실제로 적용됨
            _pending = new SettingsData();
            ApplyPendingToUI(false);
        }

        private void OnClickClose()
        {
            // _pending은 버려지고 UI만 닫힘 — 저장된 설정값은 변경되지 않음
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        private void SwitchToGame()
        {
            ServiceLocator.Get<IUIManager>().ShowUI<GameSettingUI>(PushMode.Replace);
        }

        private void SwitchToGraphic()
        {
            ServiceLocator.Get<IUIManager>().ShowUI<GraphicSettingUI>(PushMode.Replace);
        }

        private void SwitchToAccount()
        {
            ServiceLocator.Get<IUIManager>().ShowUI<AccountSettingUI>(PushMode.Replace);
        }

        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() => OnClickSave();
        protected override void HandleCancel() => OnClickClose();
    }
}
