using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using SCOdyssey.UI;
using TMPro;
using UnityEngine;

namespace SCOdyssey
{
    public class GraphicSettingUI : BaseUI
    {
        private enum Buttons
        {
            Tab_Game,
            Tab_Graphic,
            Tab_Sound,
            Tab_Account,
            Btn_ResolutionPrev,
            Btn_ResolutionNext,
            Btn_FrameRatePrev,
            Btn_FrameRateNext,
            Btn_DisplayModePrev,
            Btn_DisplayModeNext,
            Btn_Save,
            Btn_Reset,
            Btn_Close
        }

        private enum Texts
        {
            Text_ResolutionValue,
            Text_FrameRateValue,
            Text_DisplayModeValue
        }

        private static readonly (int w, int h)[] Resolutions =
        {
            (1024, 768), (1280, 720),
            (1366, 768), (1600, 900), (1920, 1080)
        };

        private static readonly int[]    FrameRateValues = { 60, 144, 240, 360, -1 };
        private static readonly string[] FrameRateLabels = { "60 fps", "144 fps", "240 fps", "360 fps", "제한 없음" };

        private static readonly string[] DisplayModeLabels = { "전체 화면", "창 모드", "테두리 없음" };

        // _pending: UI에서 변경한 값을 임시로 보관. Btn_Save를 눌러야 실제로 저장됨.
        private SettingsData _pending;
        private int _resolutionIndex;
        private int _frameRateIndex;
        private int _displayModeIndex;
        private bool _initialized;

        private void Start()
        {
            Init();
        }

        // 일회성 배선만 담당 (Bind/AddListener는 1회면 충분)
        private void Init()
        {
            BindButton(typeof(Buttons));
            Bind<TMP_Text>(typeof(Texts));

            #region Resolution
            GetButton((int)Buttons.Btn_ResolutionPrev).onClick.AddListener(() =>
            {
                if (_resolutionIndex <= 0) return;
                _pending.resolutionIndex = --_resolutionIndex;
                RefreshResolutionText();
            });
            GetButton((int)Buttons.Btn_ResolutionNext).onClick.AddListener(() =>
            {
                if (_resolutionIndex >= Resolutions.Length - 1) return;
                _pending.resolutionIndex = ++_resolutionIndex;
                RefreshResolutionText();
            });
            #endregion

            #region Frame Rate
            GetButton((int)Buttons.Btn_FrameRatePrev).onClick.AddListener(() =>
            {
                if (_frameRateIndex <= 0) return;
                _pending.targetFrameRate = FrameRateValues[--_frameRateIndex];
                RefreshFrameRateText();
            });
            GetButton((int)Buttons.Btn_FrameRateNext).onClick.AddListener(() =>
            {
                if (_frameRateIndex >= FrameRateValues.Length - 1) return;
                _pending.targetFrameRate = FrameRateValues[++_frameRateIndex];
                RefreshFrameRateText();
            });
            #endregion

            #region Display Mode
            GetButton((int)Buttons.Btn_DisplayModePrev).onClick.AddListener(() =>
            {
                if (_displayModeIndex <= 0) return;
                _pending.displayMode = --_displayModeIndex;
                RefreshDisplayModeText();
            });
            GetButton((int)Buttons.Btn_DisplayModeNext).onClick.AddListener(() =>
            {
                if (_displayModeIndex >= DisplayModeLabels.Length - 1) return;
                _pending.displayMode = ++_displayModeIndex;
                RefreshDisplayModeText();
            });
            #endregion

            GetButton((int)Buttons.Tab_Game).onClick.AddListener(SwitchToGame);
            GetButton((int)Buttons.Tab_Sound).onClick.AddListener(SwitchToSound);
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

        // 저장된 설정을 _pending에 깊은 복사로 로드한 뒤 UI에 반영
        private void RefreshFromSettings()
        {
            var current = ServiceLocator.Get<ISettingsManager>().Current;
            _pending = JsonAdapter.FromJson<SettingsData>(JsonAdapter.ToJson(current));
            ApplyPendingToUI();
        }

        // 현재 _pending 값을 모든 UI 컴포넌트에 반영 (RefreshFromSettings / OnClickReset 공용)
        private void ApplyPendingToUI()
        {
            _resolutionIndex = _pending.resolutionIndex;
            RefreshResolutionText();
            _frameRateIndex = System.Array.IndexOf(FrameRateValues, _pending.targetFrameRate);
            if (_frameRateIndex < 0) _frameRateIndex = 0;
            RefreshFrameRateText();
            _displayModeIndex = _pending.displayMode;
            RefreshDisplayModeText();
        }

        #region Resolution

        private void RefreshResolutionText()
        {
            var (w, h) = Resolutions[_resolutionIndex];
            GetText((int)Texts.Text_ResolutionValue).text = $"{w}×{h}";
        }

        #endregion

        #region Frame Rate

        private void RefreshFrameRateText()
        {
            GetText((int)Texts.Text_FrameRateValue).text = FrameRateLabels[_frameRateIndex];
        }

        #endregion

        #region Display Mode

        private void RefreshDisplayModeText()
        {
            GetText((int)Texts.Text_DisplayModeValue).text = DisplayModeLabels[_displayModeIndex];
        }

        #endregion

        private void OnClickSave()
        {
            // _pending의 값을 Current에 복사한 뒤 Apply(시스템 반영) + Save(PlayerPrefs 저장)
            var settings = ServiceLocator.Get<ISettingsManager>();
            settings.Current.resolutionIndex  = _pending.resolutionIndex;
            settings.Current.targetFrameRate  = _pending.targetFrameRate;
            settings.Current.displayMode      = _pending.displayMode;
            settings.Apply();
            settings.Save();
        }

        private void OnClickReset()
        {
            // _pending만 기본값으로 갱신 — Save를 눌러야 실제로 적용됨
            _pending = new SettingsData();
            ApplyPendingToUI();
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

        private void SwitchToSound()
        {
            ServiceLocator.Get<IUIManager>().ShowUI<SoundSettingUI>(PushMode.Replace);
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
