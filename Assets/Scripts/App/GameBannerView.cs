using TMPro;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // 화면 중앙 대형 텍스트. 클리어 등급 배너와 일시정지 재개 카운트다운이 같은 오브젝트를 쓴다.
    // 표시 시간과 순서는 호출자(GameManager 코루틴)가 정한다.
    public class GameBannerView : MonoBehaviour
    {
        public TextMeshProUGUI bannerText;

        public void ShowClear(ClearType rank)
        {
            switch (rank)
            {
                case ClearType.AllPerfect:  Show("ALL PERFECT",  Color.cyan);   break;
                case ClearType.OverMillion: Show("OVER MILLION", Color.yellow); break;
                case ClearType.FullCombo:   Show("FULL COMBO",   Color.green);  break;
                case ClearType.Clear:       Show("CLEAR",        Color.white);  break;
                case ClearType.Fail:        Show("FAILED",       Color.red);    break;
            }
        }

        public void ShowCount(int remaining) => Show(remaining.ToString(), Color.white);

        public void Hide()
        {
            if (bannerText != null)
                bannerText.gameObject.SetActive(false);
        }

        private void Show(string text, Color color)
        {
            if (bannerText == null) return;

            bannerText.text = text;
            bannerText.color = color;
            bannerText.gameObject.SetActive(true);
        }
    }
}
