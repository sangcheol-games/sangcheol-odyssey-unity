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

        // 등급 도장 스프라이트. ScoreRank enum 순서(SSS, SS, S, A, B, C, F)와 인덱스가 일치해야 함
        [Tooltip("등급 도장 스프라이트. 순서는 SSS, SS, S, A, B, C, F (ScoreRank enum 순서와 일치해야 한다)")]
        [SerializeField] private Sprite[] rankStampSprites;

        // 카운트업 연출 담당 (같은 오브젝트). 수치 조정과 연출 다시 재생은 그쪽 인스펙터에서 한다
        private ResultCountAnimator countAnimator;

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
            RankStamp     // 클리어 등급 도장
        }

        protected override void Awake()
        {
            base.Awake();

            BindText(typeof(Texts));
            BindButton(typeof(Buttons));
            BindImage(typeof(Images));

            // 버튼 클릭 이벤트 연결
            GetButton((int)Buttons.RetryButton).onClick.AddListener(OnClickRetryButton);
            GetButton((int)Buttons.SubmitButton).onClick.AddListener(OnClickSubmitButton);

            currentMusic = ServiceLocator.Get<IMusicManager>().GetCurrentMusic();

            countAnimator = GetComponent<ResultCountAnimator>();
            if (countAnimator == null)
            {
                Debug.LogWarning("[ResultUI] 프리팹에 ResultCountAnimator가 없어 기본값으로 추가합니다. 연출 수치를 조정하려면 ResultUI 프리팹 루트에 컴포넌트를 붙이세요.");
                countAnimator = gameObject.AddComponent<ResultCountAnimator>();
            }
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

            // 판정 통계 표시 (총 노트 수는 카운트 없이 즉시 표시)
            GetText((int)Texts.TotalNotesText).text = totalNotes.ToString();

            // 등급 도장 표시: 스프라이트만 여기서 정하고, 찍는 연출은 countAnimator가 마지막에 한다
            ScoreRank scoreRank = GetScoreRank(finalScore);
            Image stamp = GetImage((int)Images.RankStamp);
            stamp.sprite = rankStampSprites[(int)scoreRank];

            // 카운트업 줄 목록. 배열 순서 = 연출 순서(위 → 아래). 판정 그룹(Perfect~Umm)과 ScorePanel 그룹(Rate~Score) 사이에 텀을 둔다
            var rows = new ResultCountAnimator.Row[]
            {
                // Perfect는 ClearType과 무관하게 항상 표시
                new ResultCountAnimator.Row(GetText((int)Texts.PerfectCountText), judgeCounts[JudgeType.Perfect], FormatCount),
                new ResultCountAnimator.Row(GetText((int)Texts.MasterCountText), judgeCounts[JudgeType.Master], FormatCount),
                new ResultCountAnimator.Row(GetText((int)Texts.IdealCountText), judgeCounts[JudgeType.Ideal], FormatCount),
                new ResultCountAnimator.Row(GetText((int)Texts.KindCountText), judgeCounts[JudgeType.Kind], FormatCount),
                new ResultCountAnimator.Row(GetText((int)Texts.UmmCountText), judgeCounts[JudgeType.Umm], FormatCount),

                // 게이지 퍼센트 표시 (ScorePanel 맨 위 Rate. 여기서 새 그룹 시작)
                new ResultCountAnimator.Row(GetText((int)Texts.GaugeText), gaugePercent, FormatGauge, startsGroup: true),

                // 최대 콤보 표시
                new ResultCountAnimator.Row(GetText((int)Texts.MaxComboText), maxCombo, FormatCount),

                // 점수 표시 (7자리 포맷)
                new ResultCountAnimator.Row(GetText((int)Texts.ScoreText), finalScore, FormatScore, isFinale: true)
            };

            countAnimator.Play(rows, stamp);
        }

        private static string FormatCount(float v)
        {
            return Mathf.FloorToInt(v).ToString();
        }

        private static string FormatScore(float v)
        {
            return Mathf.FloorToInt(v).ToString("N0");
        }

        private static string FormatGauge(float v)
        {
            return $"{v:F2}%";
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
