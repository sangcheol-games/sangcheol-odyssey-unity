using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using SCOdyssey.App;
using SCOdyssey.Core;
using static SCOdyssey.Domain.Service.Constants;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.UI
{
    public class ResultUI : BaseUI
    {
        MusicSO currentMusic;

        // 텍스트 enum
        private enum Texts
        {
            MusicTitleText,    // 곡 제목
            ArtistText,        // 아티스트
            ScoreText,          // 최종 점수
            TotalNotesText,     // 총 노트 수
            GaugeText,          // 게이지 퍼센트
            MaxComboText,       // 최대 콤보
            PerfectCountText,   // Perfect 개수
            MasterCountText,    // Master 개수
            IdealCountText,     // Ideal 개수
            KindCountText,      // Kind 개수
            UmmCountText        // Umm 개수
        }

        // 버튼 enum
        private enum Buttons
        {
            RetryButton,        // 다시하기
            SubmitButton        // 확인 (곡 선택으로 돌아가기)
        }

        private enum Images
        {
            AlbumArt,     // 앨범 아트
            RankImage     // 클리어 등급 스프라이트
        }

        private enum GameObjects
        {
            Perfect
        }

        protected override void Awake()
        {
            base.Awake();

            BindText(typeof(Texts));
            BindButton(typeof(Buttons));
            BindImage(typeof(Images));
            BindObject(typeof(GameObjects));

            // 버튼 클릭 이벤트 연결
            GetButton((int)Buttons.RetryButton).onClick.AddListener(OnClickRetryButton);
            GetButton((int)Buttons.SubmitButton).onClick.AddListener(OnClickSubmitButton);

            currentMusic = ServiceLocator.Get<IMusicManager>().GetCurrentMusic();
        }

        // 결과 화면 초기화
        public void Init(
            int finalScore,
            ClearType result,
            int maxCombo,
            int totalNotes,
            Dictionary<JudgeType, int> judgeCounts,
            float gaugePercent)
        {
            // 곡 정보 표시
            GetImage((int)Images.AlbumArt).sprite = currentMusic.albumArt;
            GetText((int)Texts.MusicTitleText).text = currentMusic.title.GetLocalizedString();
            GetText((int)Texts.ArtistText).text = currentMusic.producer.GetLocalizedString();

            // 점수 표시 (7자리 포맷)
            GetText((int)Texts.ScoreText).text = finalScore.ToString("N0");

            // 등급 스프라이트 표시
            ScoreRank scoreRank = GetScoreRank(finalScore);
            GetImage((int)Images.RankImage).sprite = GetRankSprite(scoreRank);

            // 게이지 퍼센트 표시
            GetText((int)Texts.GaugeText).text = $"{gaugePercent:F2}%";

            // 최대 콤보 표시
            GetText((int)Texts.MaxComboText).text = maxCombo.ToString();

            // 판정 통계 표시
            GetText((int)Texts.TotalNotesText).text = totalNotes.ToString();

            // OverMillion 판정, perfect 비표시
            if (ServiceLocator.TryGet<ISettingsManager>(out var settingsManager) &&
                (settingsManager.Current.showPerfect || result == ClearType.OverMillion || result == ClearType.AllPerfect))
            {
                GetText((int)Texts.PerfectCountText).text = judgeCounts[JudgeType.Perfect].ToString();
            }
            else
                DisablePerfect();

            GetText((int)Texts.MasterCountText).text = judgeCounts[JudgeType.Master].ToString();
            GetText((int)Texts.IdealCountText).text = judgeCounts[JudgeType.Ideal].ToString();
            GetText((int)Texts.KindCountText).text = judgeCounts[JudgeType.Kind].ToString();
            GetText((int)Texts.UmmCountText).text = judgeCounts[JudgeType.Umm].ToString();
        }

        // finalScore → ScoreRank 계산
        private ScoreRank GetScoreRank(int finalScore)
        {
            return finalScore switch
            {
                >= 1_150_000 => ScoreRank.SSS,
                >= 1_000_000 => ScoreRank.SS,
                >= 970_000   => ScoreRank.S,
                >= 900_000   => ScoreRank.A,
                >= 800_000   => ScoreRank.B,
                >= 700_000   => ScoreRank.C,
                _            => ScoreRank.F
            };
        }

        // ScoreRank → Resources/Sprites/Rank_{rank} 스프라이트 로드
        private Sprite GetRankSprite(ScoreRank rank)
        {
            return Resources.Load<Sprite>($"Sprites/Rank_{rank}");
        }

        private void DisablePerfect()
        {
            // 필요 시 ui / 로직 수정
            GetObject((int)GameObjects.Perfect).SetActive(false);
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
