using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.App
{
    // 점수·콤보·게이지 표시. ScoreManager의 갱신 이벤트를 직접 구독
    public class HudView : MonoBehaviour
    {
        [Header("HUD")]
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI comboText;
        public TextMeshProUGUI gaugeText;
        public Image gaugeBar;      // fillAmount로 게이지 바 표현 시

        private ScoreManager _score;

        // 같은 대상이면 재구독하지 않는다
        public void Bind(ScoreManager score)
        {
            if (ReferenceEquals(_score, score)) return;

            Unbind();
            _score = score;
            if (_score == null) return;

            _score.OnScoreChanged += UpdateScore;
            _score.OnComboChanged += UpdateCombo;
            _score.OnGaugeChanged += UpdateGauge;
        }

        private void Unbind()
        {
            if (_score == null) return;

            _score.OnScoreChanged -= UpdateScore;
            _score.OnComboChanged -= UpdateCombo;
            _score.OnGaugeChanged -= UpdateGauge;
            _score = null;
        }

        private void OnDestroy() => Unbind();

        private void UpdateScore(int score)
        {
            if (scoreText != null)
                scoreText.text = score.ToString("D7");  // 7자리 숫자로 포맷 (0000000)
        }

        private void UpdateCombo(int combo)
        {
            if (comboText == null) return;

            if (combo > 0)
            {
                comboText.text = combo.ToString();
                comboText.gameObject.SetActive(true);
            }
            else
            {
                comboText.gameObject.SetActive(false);
            }
        }

        private void UpdateGauge(float percentage)
        {
            if (gaugeText != null)
            {
                // 소수점 2자리까지 표시 (100.0%)
                gaugeText.text = $"{percentage:F2}%";
                gaugeText.color = percentage >= 100f ? Color.cyan : Color.white;  // Perfect/Master 유지 중이면 강조
            }

            if (gaugeBar != null)
                gaugeBar.fillAmount = percentage / 100f;
        }
    }
}
