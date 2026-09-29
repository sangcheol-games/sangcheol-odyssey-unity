using System;
using SCOdyssey.Rhythm;
using UnityEngine;

namespace SCOdyssey.Config
{
    // 점수·클리어·랭크 규칙. 에셋은 Resources/Config/ScoreSettings.
    // 배율은 double로 둔다(float 0.7은 0.69999…라 전부 Ideal이 700,000에 못 미친다).
    [CreateAssetMenu(fileName = "ScoreSettings", menuName = "SCOdyssey/ScoreSettings")]
    public sealed class ScoreSettingsSO : ScriptableObject
    {
        public const string ResourcePath = "Config/ScoreSettings";

        [Header("점수")]
        public int maxScore = 1_000_000;

        [Header("판정 배율 (노트당 배점 x 배율)")]
        public double perfectMultiplier = 1.0;
        public double masterMultiplier = 1.0;
        public double idealMultiplier = 0.7;
        public double kindMultiplier = 0.5;
        public double ummMultiplier = 0.0;

        [Header("콤보")]
        public bool kindBreaksCombo = true;
        public bool ummBreaksCombo = true;

        [Header("클리어")]
        [Tooltip("전부 Master 이상일 때 Perfect 1개당 노트 배점 x 이 값을 더 준다")]
        public double perfectExBonus = 0.2;
        [Tooltip("최종 점수가 이 값 미만이면 Fail")]
        public int failBelowScore = 700_000;

        [Header("랭크 (최저 점수)")]
        public int rankSSS = 1_150_000;
        public int rankSS = 1_000_000;
        public int rankS = 970_000;
        public int rankA = 900_000;
        public int rankB = 800_000;
        public int rankC = 700_000;

        // Inspector -> ServiceLocator -> Resources 순으로 찾는다. 없거나 값이 잘못됐으면 코드 기본값
        public static ScoreRules Resolve(ScoreSettingsSO serialized)
        {
            ScoreSettingsSO so = ConfigLocator.Resolve(serialized, ResourcePath);
            if (so == null) return ScoreRules.Default;

            try
            {
                return so.ToRules();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[ScoreSettings] {so.name} 값이 잘못돼 기본값을 쓴다: {e.Message}");
                return ScoreRules.Default;
            }
        }

        public ScoreRules ToRules()
            => new(maxScore,
                perfectMultiplier, masterMultiplier, idealMultiplier, kindMultiplier, ummMultiplier,
                kindBreaksCombo, ummBreaksCombo,
                perfectExBonus, failBelowScore,
                new RankThresholds(rankSSS, rankSS, rankS, rankA, rankB, rankC));

        private void OnValidate()
        {
            maxScore = Mathf.Max(1, maxScore);
            perfectMultiplier = Math.Max(0, perfectMultiplier);
            masterMultiplier = Math.Max(0, masterMultiplier);
            idealMultiplier = Math.Max(0, idealMultiplier);
            kindMultiplier = Math.Max(0, kindMultiplier);
            ummMultiplier = Math.Max(0, ummMultiplier);
            perfectExBonus = Math.Max(0, perfectExBonus);
            rankSS = Mathf.Min(rankSSS, rankSS);
            rankS = Mathf.Min(rankSS, rankS);
            rankA = Mathf.Min(rankS, rankA);
            rankB = Mathf.Min(rankA, rankB);
            rankC = Mathf.Min(rankB, rankC);
        }
    }
}
