using System;
using System.Collections.Generic;
using SCOdyssey.Config;
using SCOdyssey.Game;
using SCOdyssey.Rhythm;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    // ── 흐름 (점수·콤보·게이지) ───────────────────────────────────────────────
    //
    //  게임 시작 시 GameManager가 Init(totalNotes)를 호출한다.
    //        점수 설정(ScoreSettingsSO)을 읽어 ScoreModel을 새로 만든다.
    //
    //  노트 판정마다 버스(NoteJudged) 구독으로 OnNoteJudged(e)가 호출된다. miss도 같은 이벤트로 온다.
    //        ScoreModel에 반영 -> UpdateUI()로 점수·콤보·게이지 이벤트 발행.
    //
    //  게임 종료 시 GameManager가 GetFinalScore()/GetClearRank()/GetScoreRank()로 최종 결과를 조회한다.
    //        배율·콤보 끊김·OverMillion 보정·Perfect Ex 보너스·Fail·랭크 계산은 ScoreModel/ScoreRules가 한다.
    // ──────────────────────────────────────────────────────────────────────────
    public class ScoreManager : MonoBehaviour
    {
        [Header("점수 설정 (비우면 Resources/Config/ScoreSettings)")]
        [SerializeField] private ScoreSettingsSO scoreSettings;

        private ScoreModel _model = new(ScoreRules.Default, 0);   // Init에서 설정을 읽어 새로 만든다

        // UI 갱신을 위한 이벤트
        public event Action<int> OnScoreChanged;      // 점수 (int 표기)
        public event Action<float> OnGaugeChanged;    // 게이지 (0.0 ~ 100.0)
        public event Action<int> OnComboChanged;      // 콤보

        private IJudgementBus judgementBus;

        // 게임 시작 시 총 노트 수를 받아 점수 모델을 새로 만든다(GameManager.StartGame이 호출)
        public void Init(int totalNotes, IJudgementBus bus)
        {
            BindBus(bus);

            if (totalNotes <= 0)
                Debug.LogError("Total note count is zero. Score per note set to zero.");

            ScoreRules rules = ScoreSettingsSO.Resolve(scoreSettings);
            _model = new ScoreModel(rules, Math.Max(0, totalNotes));
            Debug.Log($"[Score] {rules}");

            // 초기 UI 갱신
            UpdateUI();
        }

        // 같은 버스면 재구독하지 않는다(Init이 두 번 불려도 중복 가산 없음)
        private void BindBus(IJudgementBus bus)
        {
            if (ReferenceEquals(judgementBus, bus)) return;

            UnbindBus();
            judgementBus = bus;
            if (judgementBus == null) return;

            judgementBus.NoteJudged += OnNoteJudged;
        }

        private void UnbindBus()
        {
            if (judgementBus == null) return;

            judgementBus.NoteJudged -= OnNoteJudged;
            judgementBus = null;
        }

        private void OnDestroy() => UnbindBus();

        // 노트 1개 판정마다(버스 구독). miss는 모델이 Umm으로 센다
        private void OnNoteJudged(JudgeEvent e)
        {
            _model.Apply(e);
            AfterJudge();
        }

        // 등급 하나를 직접 반영한다
        public void ProcessJudge(JudgeType type)
        {
            _model.Apply(type);
            AfterJudge();
        }

        private void AfterJudge()
        {
            Debug.Log($"Score: {_model.Score}, Combo: {_model.Combo}");
            UpdateUI();
        }

        private void UpdateUI()
        {
            OnScoreChanged?.Invoke(_model.DisplayScore);
            OnComboChanged?.Invoke(_model.Combo);
            OnGaugeChanged?.Invoke((float)_model.GaugePercent);
        }

        // 게임 종료 시 최종 점수 (전부 Master 이상이면 최대 점수 + Perfect Ex 보너스)
        public int GetFinalScore()
        {
            if (_model.IsOverMillion)
                Debug.Log("Over Million Bonus Applied! (Perfect ExScore Added)");

            return _model.FinalScore;
        }

        // 총 노트 수 반환
        public int GetTotalNoteCount() => _model.TotalNotes;

        // 최대 콤보 수 반환
        public int GetMaxCombo() => _model.MaxCombo;

        // 판정 타입별 개수 반환 (복사본)
        public Dictionary<JudgeType, int> GetJudgeCounts() => new()
        {
            { JudgeType.Perfect, _model.CountOf(JudgeType.Perfect) },
            { JudgeType.Master, _model.CountOf(JudgeType.Master) },
            { JudgeType.Ideal, _model.CountOf(JudgeType.Ideal) },
            { JudgeType.Kind, _model.CountOf(JudgeType.Kind) },
            { JudgeType.Umm, _model.CountOf(JudgeType.Umm) }
        };

        // 현재 게이지 퍼센트 반환 (0~100)
        public float GetGaugePercent() => (float)_model.GaugePercent;

        // 클리어 등급 판정 (Fail/Clear/FullCombo/OverMillion/AllPerfect)
        public ClearType GetClearRank() => _model.ClearType;

        // 최종 점수 랭크 (SSS ~ F)
        public ScoreRank GetScoreRank() => _model.Rank;
    }
}
