using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using SCOdyssey.App;
using SCOdyssey.Core;

namespace SCOdyssey.UI
{
    /// <summary>
    /// 곡 제목·아티스트처럼 LocalizedString으로 저장된 문구를 설정의 표시 언어(displayLanguageCode)로 꺼낸다.
    /// 게임 UI 언어(현재 선택 locale)와 곡 정보 표시 언어가 따로 설정되기 때문에 GetLocalizedString()만으로는 부족하다.
    /// AdventureUI·MusicListUI·GameLoadingUI가 같은 규칙을 쓴다.
    /// </summary>
    public static class LocalizedTextUtil
    {
        /// <summary>
        /// 로딩 화면처럼 절대 던지면 안 되는 곳에서도 쓰므로 실패하면 경고만 남기고 fallback을 돌려준다.
        /// </summary>
        public static string Get(LocalizedString localizedString, string fallback = "")
        {
            if (localizedString == null) return fallback;

            try
            {
                if (!ServiceLocator.TryGet<ISettingsManager>(out var settings))
                    return localizedString.GetLocalizedString();

                // 해당 locale이 없으면 현재 선택된 locale로 폴백
                var locale = LocalizationSettings.AvailableLocales.GetLocale(settings.Current.displayLanguageCode);
                if (locale == null) return localizedString.GetLocalizedString();

                // WaitForCompletion()은 테이블 미로드 시 블로킹 발생 가능
                // 성능 이슈 시: Window > Asset Management > Localization Tables
                //               → String Table Collection 선택
                //               → Inspector에서 Preload All Tables 체크
                // Preload 활성화 시 게임 시작 시 테이블이 미리 로드되어 즉시 반환됨
                return LocalizationSettings.StringDatabase
                    .GetLocalizedStringAsync(localizedString.TableReference, localizedString.TableEntryReference, locale)
                    .WaitForCompletion();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LocalizedTextUtil] 문구를 가져오지 못했습니다: " + e.Message);
                return fallback;
            }
        }
    }
}
