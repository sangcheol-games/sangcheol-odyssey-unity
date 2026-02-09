using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using SCOdyssey.App;
using SCOdyssey.Core;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    public class ResultUI : BaseUI
    {
        // 텍스트 enum
        private enum Texts
        {
            ScoreText,          // 최종 점수
            RankText,           // 클리어 등급
            GaugeText,          // 게이지 퍼센트
            MaxComboText,       // 최대 콤보
            PerfectCountText,   // Perfect 개수
            MasterCountText,    // Master 개수
            IdealCountText,     // Ideal 개수
            KindCountText,      // Kind 개수
            UhmCountText        // Uhm 개수
        }

        // 버튼 enum
        private enum Buttons
        {
            RetryButton,        // 다시하기
            SubmitButton        // 확인 (곡 선택으로 돌아가기)
        }

        protected override void Awake()
        {
            base.Awake();

            BindText(typeof(Texts));
            BindButton(typeof(Buttons));

            // 버튼 클릭 이벤트 연결
            GetButton((int)Buttons.RetryButton).onClick.AddListener(OnClickRetryButton);
            GetButton((int)Buttons.SubmitButton).onClick.AddListener(OnClickSubmitButton);
        }

        // 결과 화면 초기화
        public void Init(
            int finalScore,
            ClearRank rank,
            int maxCombo,
            Dictionary<JudgeType, int> judgeCounts,
            float gaugePercent)
        {
            // 점수 표시 (7자리 포맷)
            GetText((int)Texts.ScoreText).text = finalScore.ToString("N0");

            // 등급 표시 (색상 포함)
            TMP_Text rankText = GetText((int)Texts.RankText);
            rankText.text = rank.ToString().ToUpper();
            rankText.color = GetRankColor(rank);

            // 게이지 퍼센트 표시
            GetText((int)Texts.GaugeText).text = $"{gaugePercent:F2}%";

            // 최대 콤보 표시
            GetText((int)Texts.MaxComboText).text = maxCombo.ToString();

            // 판정 통계 표시
            GetText((int)Texts.PerfectCountText).text = judgeCounts[JudgeType.Perfect].ToString();
            GetText((int)Texts.MasterCountText).text = judgeCounts[JudgeType.Master].ToString();
            GetText((int)Texts.IdealCountText).text = judgeCounts[JudgeType.Ideal].ToString();
            GetText((int)Texts.KindCountText).text = judgeCounts[JudgeType.Kind].ToString();
            GetText((int)Texts.UhmCountText).text = judgeCounts[JudgeType.Uhm].ToString();
        }

        // 등급별 색상 반환
        private Color GetRankColor(ClearRank rank)
        {
            return rank switch
            {
                ClearRank.AllPerfect => Color.cyan,
                ClearRank.OverMillion => Color.yellow,
                ClearRank.FullCombo => Color.green,
                ClearRank.Clear => Color.white,
                ClearRank.Fail => Color.red,
                _ => Color.white
            };
        }

        // 다시하기 버튼 클릭
        private void OnClickRetryButton()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            SceneManager.LoadScene("GameScene");
        }

        // 확인 버튼 클릭 (곡 선택으로 돌아가기)
        private void OnClickSubmitButton()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            SceneManager.LoadScene("MainScene");
        }

        // BaseUI 추상 메서드 구현 (현재 미사용)
        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit()
        {
            OnClickSubmitButton();
        }
        protected override void HandleCancel()
        {
            OnClickSubmitButton();
        }
    }
}
