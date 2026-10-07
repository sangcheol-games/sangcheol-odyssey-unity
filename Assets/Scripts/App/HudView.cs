using SCOdyssey.Game;
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
        public GameObject comboRoot;   // Combo 그룹 루트("Combo" 라벨 + 숫자 + 인디케이터). 콤보 0이면 통째로 숨긴다
        public TextMeshProUGUI comboText;
        public ComboIndicatorAnimator comboIndicator; // Combo 화살표 밀림 연출. 미할당이면 연출만 생략된다
        public ComboCountAnimator comboCountAnimator; // 콤보 숫자 팝 연출. 미할당이면 연출만 생략된다
        public TextMeshProUGUI gaugeText;
        public Image gaugeBar;      // fillAmount로 게이지 바 표현 시

        private ScoreManager _score;

        // 같은 대상이면 재구독하지 않는다
        public void Bind(ScoreManager score)
        {
            if (ReferenceEquals(_score, score)) return;

            Unbind();
            _score = score;
            if (_score != null)
            {
                _score.OnScoreChanged += UpdateScore;
                _score.OnComboChanged += UpdateCombo;
                _score.OnGaugeChanged += UpdateGauge;
            }

            // ScoreManager.Init은 로딩 화면이 걷힌 뒤에야 불린다. 그 사이 게임 화면이 먼저 보이므로
            // 씬에 켜진 채 저장된 Combo 그룹을 여기서 미리 숨긴다
            UpdateCombo(0);
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
            // 숨기기 전에 텍스트를 먼저 갱신해야 다시 켜질 때 이전 값이 한 프레임 노출되지 않는다
            if (combo > 0 && comboText != null)
                comboText.text = combo.ToString();

            // 콤보 0이면 Combo 하위(라벨/숫자/인디케이터)를 통째로 숨긴다
            if (comboRoot != null)
                comboRoot.SetActive(combo > 0);
            else if (comboText != null)
                comboText.gameObject.SetActive(combo > 0);   // comboRoot 미할당 시 기존 동작으로 폴백

            // 인디케이터 밀림 연출과 숫자 팝 연출. 반드시 SetActive 이후에 호출한다
            // (SetActive(true)가 OnEnable을 동기 실행해 위상/포즈를 리셋하므로, 먼저 부르면 이번 콤보가 지워진다)
            // OnComboChanged는 판정마다 같은 값·0으로도 재발행되므로 증가 판별은 각 컴포넌트가 한다
            if (comboIndicator != null)
                comboIndicator.SetCombo(combo);
            if (comboCountAnimator != null)
                comboCountAnimator.SetCombo(combo);
        }

        private void UpdateGauge(float percentage)
        {
            if (gaugeText != null)
            {
                // 소수점 2자리까지 표시 (100.00%)
                gaugeText.text = $"{percentage:F2}%";
                gaugeText.color = percentage >= 100f ? Color.cyan : Color.white;  // Perfect/Master 유지 중이면 강조
            }

            if (gaugeBar != null)
                gaugeBar.fillAmount = percentage / 100f;
        }
    }
}
