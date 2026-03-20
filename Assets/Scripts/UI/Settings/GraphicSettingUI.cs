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

        // 16:9 고정 해상도 목록
        private static readonly (int w, int h)[] Resolutions =
        {
            (1024, 576), (1152, 648), (1280, 720),
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

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            Bind<TMP_Text>(typeof(Texts));

            // 현재 저장된 설정을 깊은 복사 → _pending에 저장
            var current = ServiceLocator.Get<ISettingsManager>().Current;
            _pending = JsonAdapter.FromJson<SettingsData>(JsonAdapter.ToJson(current));

            #region Resolution
            _resolutionIndex = _pending.resolutionIndex;
            RefreshResolutionText();

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
            _frameRateIndex = System.Array.IndexOf(FrameRateValues, _pending.targetFrameRate);
            if (_frameRateIndex < 0) _frameRateIndex = 0;
            RefreshFrameRateText();

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
            _displayModeIndex = _pending.displayMode;
            RefreshDisplayModeText();

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
            _resolutionIndex = _pending.resolutionIndex;
            RefreshResolutionText();
            _frameRateIndex = System.Array.IndexOf(FrameRateValues, _pending.targetFrameRate);
            if (_frameRateIndex < 0) _frameRateIndex = 0;
            RefreshFrameRateText();
            _displayModeIndex = _pending.displayMode;
            RefreshDisplayModeText();
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

        private void SwitchToSound()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<SoundSettingUI>();
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
