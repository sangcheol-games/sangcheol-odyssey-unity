using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    // 게임이 실제로 읽는 판정 설정 에셋. SO 타입은 Assembly-CSharp에 있어서 필드를 이름으로 읽는다.
    public class JudgeSettingsAssetTests
    {
        private const string AssetPath = "Assets/Resources/Config/JudgeSettings.asset";

        [Test]
        public void ShippedAsset_LoadsAndMatchesDefaults()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath);
            Assert.That(asset, Is.Not.Null, AssetPath);
            Assert.That(asset.GetType().FullName, Is.EqualTo("SCOdyssey.Config.JudgeSettingsSO"));

            var so = new SerializedObject(asset);
            float Ms(string field) => so.FindProperty(field).floatValue;
            JudgeWindows windows = JudgeWindows.FromMilliseconds(Ms("perfectMs"), Ms("masterMs"), Ms("idealMs"), Ms("kindMs"), Ms("ummMs"));

            // 판정감을 일부러 바꿨다면 여기 기대값도 같이 바꾼다
            Assert.That(windows, Is.EqualTo(JudgeWindows.Default));
            Assert.That((NoteSelectPolicy)so.FindProperty("selectPolicy").enumValueIndex, Is.EqualTo(NoteSelectPolicy.Earliest));
            Assert.That(so.FindProperty("tailWindowScale").floatValue, Is.EqualTo(1f));
        }
    }
}
