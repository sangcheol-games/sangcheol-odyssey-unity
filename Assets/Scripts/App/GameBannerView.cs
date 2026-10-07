using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // 화면 중앙 대형 이미지. 클리어 등급 배너와 일시정지 재개 카운트다운이 같은 Image를 쓴다.
    // 표시 시간과 순서는 호출자(GameManager 코루틴)가 정하고, 등급별 색상은 스프라이트가 담당한다.
    public class GameBannerView : MonoBehaviour
    {
        public Image clearEffectImage;

        // ClearType enum 순서(Fail, Clear, FullCombo, OverMillion, AllPerfect)와 인덱스가 일치해야 함
        [SerializeField] private Sprite[] clearTypeSprites = new Sprite[5];

        // 일시정지 재개 카운트다운. GameUI_Countdown_1~3, 인덱스 = 숫자 - 1
        [SerializeField] private Sprite[] resumeCountdownSprites = new Sprite[3];

        public void ShowClear(ClearType rank) => Show(clearTypeSprites, (int)rank, $"clearTypeSprites[{rank}]");

        public void ShowCount(int remaining) => Show(resumeCountdownSprites, remaining - 1, $"resumeCountdownSprites[{remaining - 1}]");

        public void Hide()
        {
            if (clearEffectImage != null)
                clearEffectImage.gameObject.SetActive(false);
        }

        // 스프라이트가 없으면 이전 그림이 남지 않게 숨기고 경고만 남긴다
        private void Show(Sprite[] sprites, int index, string what)
        {
            if (clearEffectImage == null) return;

            Sprite sprite = sprites != null && (uint)index < (uint)sprites.Length ? sprites[index] : null;
            if (sprite == null)
            {
                Debug.LogWarning($"[GameBannerView] {what} 스프라이트가 없습니다. 표시를 건너뜁니다.");
                Hide();
                return;
            }

            clearEffectImage.sprite = sprite;
            clearEffectImage.color = Color.white;
            clearEffectImage.gameObject.SetActive(true);
        }
    }
}
