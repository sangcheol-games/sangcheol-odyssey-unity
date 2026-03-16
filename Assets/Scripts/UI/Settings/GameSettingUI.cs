using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.UI;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace SCOdyssey
{
    public class GameSettingUI : BaseUI
    {
        private enum Buttons
        {
            Tab_Game,
            Tab_Graphic,
            Tab_Sound,
            Tab_Account,
            Btn_LanguagePrev,
            Btn_LanguageNext,
            Btn_DisplayLanguagePrev,
            Btn_DisplayLanguageNext,
            Btn_Close,
        }

        private enum Texts
        {
            Text_LanguageValue,
            Text_DisplayLanguageValue,
            Text_BgaOpacityValue,
            Text_NoteOpacityValue,
        }

        private enum Sliders
        {
            Slider_BgaOpacity,
            Slider_NoteOpacity,
        }

        // 기본 언어 목록 (BCP 47)
        private static readonly string[] LanguageCodes  = { "ko-KR", "ja-JP", "en-US" };
        private static readonly string[] LanguageLabels = { "한국어", "日本語", "English" };

        // 곡 표시 언어 목록 (original = 원제)
        private static readonly string[] DisplayLanguageCodes  = { "origin", "ko-KR", "ja-JP", "en-US" };
        private static readonly string[] DisplayLanguageLabels = { "원제", "한국어", "日本語", "English" };

        private int _languageIndex;
        private int _displayLanguageIndex;

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            BindText(typeof(Texts));
            Bind<Slider>(typeof(Sliders));

            var current = ServiceLocator.Get<ISettingsManager>().Current;

            // 저장된 기본 언어 코드로 인덱스 초기화
            _languageIndex = Mathf.Max(0, System.Array.IndexOf(LanguageCodes, current.languageCode));
            UpdateLanguageLabel();

            // 저장된 표시 언어 코드로 인덱스 초기화
            _displayLanguageIndex = Mathf.Max(0, System.Array.IndexOf(DisplayLanguageCodes, current.displayLanguageCode));
            UpdateDisplayLanguageLabel();

            // 기본 언어 Arrow Selector
            GetButton((int)Buttons.Btn_LanguagePrev).onClick.AddListener(OnLanguagePrev);
            GetButton((int)Buttons.Btn_LanguageNext).onClick.AddListener(OnLanguageNext);

            // 표시 언어 Arrow Selector
            GetButton((int)Buttons.Btn_DisplayLanguagePrev).onClick.AddListener(OnDisplayLanguagePrev);
            GetButton((int)Buttons.Btn_DisplayLanguageNext).onClick.AddListener(OnDisplayLanguageNext);

            // 탭 버튼
            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Sound).onClick.AddListener(SwitchToSound);
            GetButton((int)Buttons.Tab_Account).onClick.AddListener(SwitchToAccount);

            // BGA 투명도 슬라이더 (0 ~ 1, 기본값 1)
            var bgaSlider = Get<Slider>((int)Sliders.Slider_BgaOpacity);
            bgaSlider.minValue = 0f;
            bgaSlider.maxValue = 1f;
            bgaSlider.value = current.bgaOpacity;
            UpdateBgaOpacityLabel(current.bgaOpacity);
            bgaSlider.onValueChanged.AddListener(v =>
            {
                UpdateBgaOpacityLabel(v);
                var settings = ServiceLocator.Get<ISettingsManager>();
                settings.Current.bgaOpacity = v;
                settings.Save();
            });

            // 고스트 노트 투명도 슬라이더 (슬라이더 0~1 → 실제 opacity 0~0.5 매핑)
            var noteSlider = Get<Slider>((int)Sliders.Slider_NoteOpacity);
            noteSlider.minValue = 0f;
            noteSlider.maxValue = 1f;
            noteSlider.value = current.noteOpacity / 0.5f;
            UpdateNoteOpacityLabel(noteSlider.value);
            noteSlider.onValueChanged.AddListener(v =>
            {
                UpdateNoteOpacityLabel(v);
                var settings = ServiceLocator.Get<ISettingsManager>();
                settings.Current.noteOpacity = v * 0.5f;
                settings.Save();
            });

            // 닫기 버튼
            GetButton((int)Buttons.Btn_Close).onClick.AddListener(OnClickClose);
        }

        #region Opacity

        private void UpdateBgaOpacityLabel(float value)
        {
            GetText((int)Texts.Text_BgaOpacityValue).text = $"{Mathf.RoundToInt(value * 100)}%";
        }

        private void UpdateNoteOpacityLabel(float value)
        {
            GetText((int)Texts.Text_NoteOpacityValue).text = $"{Mathf.RoundToInt(value * 100)}%";
        }

        #endregion

        #region Language

        private void OnLanguagePrev()
        {
            _languageIndex = (_languageIndex - 1 + LanguageCodes.Length) % LanguageCodes.Length;
            ApplyLanguage();
        }

        private void OnLanguageNext()
        {
            _languageIndex = (_languageIndex + 1) % LanguageCodes.Length;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            UpdateLanguageLabel();

            // Unity Localization 즉시 적용
            var locale = LocalizationSettings.AvailableLocales.GetLocale(LanguageCodes[_languageIndex]);
            if (locale != null)
                LocalizationSettings.SelectedLocale = locale;

            // 설정 저장
            var settings = ServiceLocator.Get<ISettingsManager>();
            settings.Current.languageCode = LanguageCodes[_languageIndex];
            settings.Save();
        }

        private void UpdateLanguageLabel()
        {
            GetText((int)Texts.Text_LanguageValue).text = LanguageLabels[_languageIndex];
        }

        private void OnDisplayLanguagePrev()
        {
            _displayLanguageIndex = (_displayLanguageIndex - 1 + DisplayLanguageCodes.Length) % DisplayLanguageCodes.Length;
            ApplyDisplayLanguage();
        }

        private void OnDisplayLanguageNext()
        {
            _displayLanguageIndex = (_displayLanguageIndex + 1) % DisplayLanguageCodes.Length;
            ApplyDisplayLanguage();
        }

        private void ApplyDisplayLanguage()
        {
            UpdateDisplayLanguageLabel();

            var settings = ServiceLocator.Get<ISettingsManager>();
            settings.Current.displayLanguageCode = DisplayLanguageCodes[_displayLanguageIndex];
            settings.Save();
        }

        private void UpdateDisplayLanguageLabel()
        {
            GetText((int)Texts.Text_DisplayLanguageValue).text = DisplayLanguageLabels[_displayLanguageIndex];
        }

        #endregion

        private void SwitchToGraphic()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.ShowUI<GraphicSettingUI>();
            uiManager.CloseUI(this);
        }

        private void SwitchToSound()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.ShowUI<SoundSettingUI>();
            uiManager.CloseUI(this);
        }

        private void SwitchToAccount()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.ShowUI<AccountSettingUI>();
            uiManager.CloseUI(this);
        }

        private void OnClickClose()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() { }
        protected override void HandleCancel() => OnClickClose();
    }
}
