using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    public class MusicListUI : BaseUI
    {
        private enum Texts
        {
            Title,
            Artist,
            Level_Easy,
            Level_Normal,
            Level_Hard,
            Level_Extreme
        }

        private enum DiffImages  // 난이도 컨테이너 배경 이미지
        {
            Easy,
            Normal,
            Hard,
            Extreme
        }

        private Image backgroundImage;  // 루트 배경 이미지

        private static readonly Color SELECTED_BG_COLOR = new Color(0.15f, 0.15f, 0.4f, 1f);
        private static readonly Color DEFAULT_BG_COLOR = new Color(0.047f, 0.047f, 0.192f, 1f);
        private static readonly Color SELECTED_DIFFICULTY_COLOR = Color.yellow;
        private static readonly Color DEFAULT_DIFFICULTY_COLOR = Color.white;
        private static readonly Color UNAVAILABLE_DIFFICULTY_COLOR = new Color(0.4f, 0.4f, 0.4f, 0.6f);

        protected override void Awake()
        {
            base.Awake();
            BindText(typeof(Texts));
            BindImage(typeof(DiffImages));
            backgroundImage = GetComponent<Image>();
        }

        // 슬롯은 입력을 직접 처리하지 않음 - AdventureUI가 담당
        protected override void OnEnable() { }
        protected override void OnDisable() { }
        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() { }
        protected override void HandleCancel() { }

        private string GetLocalizedText(UnityEngine.Localization.LocalizedString localizedString, string fallback = "")
        {
            if (localizedString == null) return fallback;

            var displayCode = ServiceLocator.Get<ISettingsManager>().Current.displayLanguageCode;
            var locale = LocalizationSettings.AvailableLocales.GetLocale(displayCode);

            // 해당 locale이 없으면 현재 선택된 locale로 폴백
            if (locale == null)
                return localizedString.GetLocalizedString();

            // WaitForCompletion()은 테이블 미로드 시 블로킹 발생 가능
            // 성능 이슈 시: Window > Asset Management > Localization Tables
            //               → String Table Collection 선택
            //               → Inspector에서 Preload All Tables 체크
            // Preload 활성화 시 게임 시작 시 테이블이 미리 로드되어 즉시 반환됨
            return LocalizationSettings.StringDatabase
                .GetLocalizedStringAsync(localizedString.TableReference, localizedString.TableEntryReference, locale)
                .WaitForCompletion();
        }

        /// <summary>
        /// 곡 데이터 및 선택 상태를 표시합니다.
        /// </summary>
        public void SetData(MusicSO music, bool isSelected = false, Difficulty selectedDifficulty = Difficulty.Easy)
        {
            if (music == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            GetText((int)Texts.Title).text = GetLocalizedText(music.title, music.name);
            GetText((int)Texts.Artist).text = GetLocalizedText(music.producer);

            // 곡 선택 하이라이트
            if (backgroundImage != null)
                backgroundImage.color = isSelected ? SELECTED_BG_COLOR : DEFAULT_BG_COLOR;

            // 4단계 난이도 레벨 표시 + 하이라이트
            for (int i = 0; i < 4; i++)
            {
                Difficulty diff = (Difficulty)i;
                int lv = 0;
                bool isAvailable = music.level != null && music.level.TryGetValue(diff, out lv) && lv != -1;
                bool isDiffSelected = isSelected && (int)selectedDifficulty == i;

                TMP_Text levelText = GetText((int)Texts.Level_Easy + i);
                if (levelText != null)
                {
                    levelText.text = isAvailable ? lv.ToString() : "-";
                    levelText.color = !isAvailable        ? UNAVAILABLE_DIFFICULTY_COLOR
                                    : isDiffSelected      ? SELECTED_DIFFICULTY_COLOR
                                    :                       DEFAULT_DIFFICULTY_COLOR;
                }

                Image diffBg = GetImage(i);
                if (diffBg != null)
                    diffBg.color = isDiffSelected ? SELECTED_BG_COLOR : DEFAULT_BG_COLOR;
            }
        }
    }
}
