using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    // 게임 화면이 실제로 읽는 연출 설정 에셋. SO 타입은 Assembly-CSharp에 있어서 필드를 이름으로 읽는다.
    public class PlayfieldSettingsAssetTests
    {
        private const string AssetPath = "Assets/Resources/Config/PlayfieldSettings.asset";

        [Test]
        public void ShippedAsset_LoadsAndMatchesDefaults()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath);
            Assert.That(asset, Is.Not.Null, AssetPath);
            Assert.That(asset.GetType().FullName, Is.EqualTo("SCOdyssey.Config.PlayfieldSettingsSO"));

            var so = new SerializedObject(asset);
            float F(string field) => so.FindProperty(field).floatValue;
            int I(string field) => so.FindProperty(field).intValue;

            // 연출을 일부러 바꿨다면 여기 기대값도 같이 바꾼다
            Assert.That(F("ghostBrightnessFallback"), Is.EqualTo(0.3f));
            Assert.That(F("hiddenToGhostOffsetPx"), Is.EqualTo(20f));
            Assert.That(F("brokenHoldAlpha"), Is.EqualTo(0.3f));
            Assert.That(F("timelineScreenMarginPx"), Is.EqualTo(100f));
            Assert.That(I("countdownBeats"), Is.EqualTo(3));
            Assert.That(so.FindProperty("countdownEpsilonBeats").doubleValue, Is.EqualTo(0.01));
            Assert.That(F("clearBannerSec"), Is.EqualTo(4f));
            Assert.That(I("resumeCountFrom"), Is.EqualTo(3));
            Assert.That(F("resumeCountSec"), Is.EqualTo(1f));
        }

        [Test]
        public void GhostFallback_IsBrightness_NotAlpha()
        {
            // Ghost는 알파가 아니라 RGB 배율로 어둡게 보인다. 옛 알파 필드가 남아 있으면 안 된다
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath));

            Assert.That(so.FindProperty("ghostBrightnessFallback"), Is.Not.Null);
            Assert.That(so.FindProperty("ghostAlphaFallback"), Is.Null);
        }

        [Test]
        public void CountdownThreshold_IsTheOldLiteral()
        {
            // 카운트다운이 쓰던 상수 3.01과 비트 단위로 같아야 경계 동작이 그대로다
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath));
            double threshold = so.FindProperty("countdownBeats").intValue + so.FindProperty("countdownEpsilonBeats").doubleValue;

            Assert.That(threshold, Is.EqualTo(3.01));
        }
    }
}
