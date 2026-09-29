using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    // 게임이 실제로 읽는 점수 설정 에셋. SO 타입은 Assembly-CSharp에 있어서 필드를 이름으로 읽는다.
    public class ScoreSettingsAssetTests
    {
        private const string AssetPath = "Assets/Resources/Config/ScoreSettings.asset";

        [Test]
        public void ShippedAsset_LoadsAndMatchesDefaults()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath);
            Assert.That(asset, Is.Not.Null, AssetPath);
            Assert.That(asset.GetType().FullName, Is.EqualTo("SCOdyssey.Config.ScoreSettingsSO"));

            var so = new SerializedObject(asset);
            int Int(string field) => so.FindProperty(field).intValue;
            double Double(string field) => so.FindProperty(field).doubleValue;
            bool Bool(string field) => so.FindProperty(field).boolValue;

            var rules = new ScoreRules(Int("maxScore"),
                Double("perfectMultiplier"), Double("masterMultiplier"), Double("idealMultiplier"), Double("kindMultiplier"), Double("ummMultiplier"),
                Bool("kindBreaksCombo"), Bool("ummBreaksCombo"),
                Double("perfectExBonus"), Int("failBelowScore"),
                new RankThresholds(Int("rankSSS"), Int("rankSS"), Int("rankS"), Int("rankA"), Int("rankB"), Int("rankC")));

            // 점수 규칙을 일부러 바꿨다면 여기 기대값도 같이 바꾼다
            Assert.That(rules.ToString(), Is.EqualTo(ScoreRules.Default.ToString()));
            Assert.That(rules, Is.EqualTo(ScoreRules.Default));
        }
    }
}
