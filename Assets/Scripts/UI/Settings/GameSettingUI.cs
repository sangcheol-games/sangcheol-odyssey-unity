using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
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
            Btn_Save,
            Btn_Reset,
            Btn_Close,
        }

        private enum Texts
        {
            Text_LanguageValue,
            Text_DisplayLanguageValue,
            Text_BgaOpacityValue,
            Text_NoteOpacityValue,
            Text_NoteSyncValue,
            Text_JudgmentSyncValue,
        }

        private enum Sliders
        {
            Slider_BgaOpacity,
            Slider_NoteOpacity,
            Slider_NoteSync,
            Slider_JudgmentSync,
        }

        // 기본 언어 목록 (BCP 47)
        private static readonly string[] LanguageCodes  = { "ko-KR", "ja-JP", "en-US" };
        private static readonly string[] LanguageLabels = { "한국어", "日本語", "English" };

        // 곡 표시 언어 목록 (original = 원제)
        private static readonly string[] DisplayLanguageCodes  = { "origin", "ko-KR", "ja-JP", "en-US" };
        private static readonly string[] DisplayLanguageLabels = { "원제", "한국어", "日本語", "English" };

        // _pending: UI에서 변경한 값을 임시로 보관. Btn_Save를 눌러야 실제로 저장됨.
        // Btn_Close를 누르면 _pending은 버려지고 기존 설정값이 유지됨.
        private SettingsData _pending;
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

            // 현재 저장된 설정을 JSON 직렬화/역직렬화로 깊은 복사 → _pending에 저장
            var current = ServiceLocator.Get<ISettingsManager>().Current;
            _pending = JsonAdapter.FromJson<SettingsData>(JsonAdapter.ToJson(current));

            #region Language
            _languageIndex = Mathf.Max(0, System.Array.IndexOf(LanguageCodes, _pending.languageCode));
            UpdateLanguageLabel();
            GetButton((int)Buttons.Btn_LanguagePrev).onClick.AddListener(OnLanguagePrev);
            GetButton((int)Buttons.Btn_LanguageNext).onClick.AddListener(OnLanguageNext);

            _displayLanguageIndex = Mathf.Max(0, System.Array.IndexOf(DisplayLanguageCodes, _pending.displayLanguageCode));
            UpdateDisplayLanguageLabel();
            GetButton((int)Buttons.Btn_DisplayLanguagePrev).onClick.AddListener(OnDisplayLanguagePrev);
            GetButton((int)Buttons.Btn_DisplayLanguageNext).onClick.AddListener(OnDisplayLanguageNext);
            #endregion

            #region Opacity
            var bgaSlider = Get<Slider>((int)Sliders.Slider_BgaOpacity);
            bgaSlider.minValue = 0f;
            bgaSlider.maxValue = 1f;
            bgaSlider.value = _pending.bgaOpacity;
            UpdateBgaOpacityLabel(_pending.bgaOpacity);
            bgaSlider.onValueChanged.AddListener(v =>
            {
                _pending.bgaOpacity = v;
                UpdateBgaOpacityLabel(v);
            });

            var noteSlider = Get<Slider>((int)Sliders.Slider_NoteOpacity);
            noteSlider.minValue = 0f;
            noteSlider.maxValue = 1f;
            noteSlider.value = _pending.noteOpacity / 0.5f;
            UpdateNoteOpacityLabel(noteSlider.value);
            noteSlider.onValueChanged.AddListener(v =>
            {
                _pending.noteOpacity = v * 0.5f;
                UpdateNoteOpacityLabel(v);
            });
            #endregion

            #region Sync
            var noteSyncSlider = Get<Slider>((int)Sliders.Slider_NoteSync);
            noteSyncSlider.minValue = -200f;
            noteSyncSlider.maxValue = 200f;
            noteSyncSlider.wholeNumbers = true;
            noteSyncSlider.value = _pending.audioOffsetMs;
            UpdateNoteSyncLabel(_pending.audioOffsetMs);
            noteSyncSlider.onValueChanged.AddListener(v =>
            {
                int ms = Mathf.RoundToInt(v);
                _pending.audioOffsetMs = ms;
                UpdateNoteSyncLabel(ms);
            });

            var judgmentSlider = Get<Slider>((int)Sliders.Slider_JudgmentSync);
            judgmentSlider.minValue = -20f;
            judgmentSlider.maxValue = 20f;
            judgmentSlider.wholeNumbers = true;
            judgmentSlider.value = _pending.judgmentOffset;
            UpdateJudgmentSyncLabel(_pending.judgmentOffset);
            judgmentSlider.onValueChanged.AddListener(v =>
            {
                int step = Mathf.RoundToInt(v);
                _pending.judgmentOffset = step;
                UpdateJudgmentSyncLabel(step);
            });
            #endregion

            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Sound).onClick.AddListener(SwitchToSound);
            GetButton((int)Buttons.Tab_Account).onClick.AddListener(SwitchToAccount);

            GetButton((int)Buttons.Btn_Save).onClick.AddListener(OnClickSave);
            GetButton((int)Buttons.Btn_Reset).onClick.AddListener(OnClickReset);
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

        #region Sync

        private void UpdateNoteSyncLabel(int ms)
        {
            string sign = ms > 0 ? "+" : "";
            GetText((int)Texts.Text_NoteSyncValue).text = $"{sign}{ms}ms";
        }

        private void UpdateJudgmentSyncLabel(int step)
        {
            string sign = step > 0 ? "+" : "";
            GetText((int)Texts.Text_JudgmentSyncValue).text = $"{sign}{step}";
        }

        #endregion

        #region Language

        private void OnLanguagePrev()
        {
            if (_languageIndex <= 0) return;
            _pending.languageCode = LanguageCodes[--_languageIndex];
            UpdateLanguageLabel();
        }

        private void OnLanguageNext()
        {
            if (_languageIndex >= LanguageCodes.Length - 1) return;
            _pending.languageCode = LanguageCodes[++_languageIndex];
            UpdateLanguageLabel();
        }

        private void UpdateLanguageLabel()
        {
            GetText((int)Texts.Text_LanguageValue).text = LanguageLabels[_languageIndex];
        }

        private void OnDisplayLanguagePrev()
        {
            if (_displayLanguageIndex <= 0) return;
            _pending.displayLanguageCode = DisplayLanguageCodes[--_displayLanguageIndex];
            UpdateDisplayLanguageLabel();
        }

        private void OnDisplayLanguageNext()
        {
            if (_displayLanguageIndex >= DisplayLanguageCodes.Length - 1) return;
            _pending.displayLanguageCode = DisplayLanguageCodes[++_displayLanguageIndex];
            UpdateDisplayLanguageLabel();
        }

        private void UpdateDisplayLanguageLabel()
        {
            GetText((int)Texts.Text_DisplayLanguageValue).text = DisplayLanguageLabels[_displayLanguageIndex];
        }

        #endregion

        private void OnClickSave()
        {
            var settings = ServiceLocator.Get<ISettingsManager>();

            // UI 텍스트가 갱신을 위해 Unity Localization의 SelectedLocale 변경
            var locale = LocalizationSettings.AvailableLocales.GetLocale(_pending.languageCode);
            if (locale != null)
                LocalizationSettings.SelectedLocale = locale;

            // _pending의 값을 Current에 복사한 뒤 Apply(시스템 반영) + Save(PlayerPrefs 저장)
            settings.Current.languageCode        = _pending.languageCode;
            settings.Current.displayLanguageCode = _pending.displayLanguageCode;
            settings.Current.bgaOpacity          = _pending.bgaOpacity;
            settings.Current.noteOpacity         = _pending.noteOpacity;
            settings.Current.audioOffsetMs       = _pending.audioOffsetMs;
            settings.Current.judgmentOffset      = _pending.judgmentOffset;
            settings.Apply();
            settings.Save();
        }

        private void OnClickReset()
        {
            _pending = new SettingsData();

            // 모든 UI 컴포넌트를 기본값으로 갱신
            _languageIndex = Mathf.Max(0, System.Array.IndexOf(LanguageCodes, _pending.languageCode));
            UpdateLanguageLabel();

            _displayLanguageIndex = Mathf.Max(0, System.Array.IndexOf(DisplayLanguageCodes, _pending.displayLanguageCode));
            UpdateDisplayLanguageLabel();

            Get<Slider>((int)Sliders.Slider_BgaOpacity).value   = _pending.bgaOpacity;
            Get<Slider>((int)Sliders.Slider_NoteOpacity).value  = _pending.noteOpacity / 0.5f;
            Get<Slider>((int)Sliders.Slider_NoteSync).value     = _pending.audioOffsetMs;
            Get<Slider>((int)Sliders.Slider_JudgmentSync).value = _pending.judgmentOffset;
        }

        private void OnClickClose()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        private void SwitchToGraphic()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<GraphicSettingUI>();
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
