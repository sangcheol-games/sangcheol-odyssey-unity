using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    // int로 저장되거나(records.json) 스프라이트 배열 인덱스로 쓰이는 enum의 순서. 멤버를 옮기거나 끼워 넣으면 여기서 깨진다.
    // 씬·프리팹 쪽 타입은 Assembly-CSharp에 있어 씬은 텍스트로, 프리팹은 SerializedObject로 읽고 스프라이트 파일명으로 순서를 본다.
    public class EnumOrderTests
    {
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string ResultUIPrefabPath = "Assets/Resources/Prefabs/UI/ResultUI.prefab";
        private const string GameBannerViewScriptGuid = "fa1392d12b3b827419f2d8b6ad785cf5";   // App/GameBannerView.cs.meta

        private static readonly Regex GuidPattern = new Regex(@"guid: ([0-9a-f]{32})");

        [Test]
        public void Difficulty_IsEasyNormalHardExtreme()
        {
            // records.json의 UserMusicRecord.difficulty, GameLoadingUI 난이도 스프라이트 배열
            AssertOrder<Difficulty>("Easy", "Normal", "Hard", "Extreme");
        }

        [Test]
        public void ClearType_IsFailClearFullComboOverMillionAllPerfect()
        {
            // records.json의 bestClearType(순서로 대소 비교), GameBannerView·MusicListUI의 clearTypeSprites
            AssertOrder<ClearType>("Fail", "Clear", "FullCombo", "OverMillion", "AllPerfect");
        }

        [Test]
        public void ScoreRank_IsSSS_SS_S_A_B_C_F()
        {
            // ResultUI.rankStampSprites, RankThresholds.RankOf의 내림차순 경계
            AssertOrder<ScoreRank>("SSS", "SS", "S", "A", "B", "C", "F");
        }

        [Test]
        public void GameScene_GameBannerView_ClearTypeSprites_FollowClearTypeOrder()
        {
            // clearTypeSprites[(int)ClearType.X] = GameUI_Result_X
            string[] expected = { "GameUI_Result_FAIL", "GameUI_Result_CLEAR", "GameUI_Result_FULLCOMBO", "GameUI_Result_OVERMILLION", "GameUI_Result_ALLPERFECT" };
            Assert.That(expected, Has.Length.EqualTo(Enum.GetValues(typeof(ClearType)).Length));

            string[] actual = FileNames(SceneArrayGuids(GameScenePath, GameBannerViewScriptGuid, "clearTypeSprites"));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void GameScene_GameBannerView_ResumeCountdownSprites_AreOneToThree()
        {
            // ShowCount(remaining)는 resumeCountdownSprites[remaining - 1]을 띄운다: 인덱스 0부터 1, 2, 3
            string[] expected = { "GameUI_Countdown_1", "GameUI_Countdown_2", "GameUI_Countdown_3" };

            string[] actual = FileNames(SceneArrayGuids(GameScenePath, GameBannerViewScriptGuid, "resumeCountdownSprites"));
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ResultUIPrefab_RankStampSprites_FollowScoreRankOrder()
        {
            // rankStampSprites[(int)ScoreRank.X] = resultUI_rank_x
            string[] expected = { "resultUI_rank_sss", "resultUI_rank_ss", "resultUI_rank_s", "resultUI_rank_a", "resultUI_rank_b", "resultUI_rank_c", "resultUI_rank_f" };
            Assert.That(expected, Has.Length.EqualTo(Enum.GetValues(typeof(ScoreRank)).Length));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResultUIPrefabPath);
            Assert.That(prefab, Is.Not.Null, ResultUIPrefabPath);
            Component resultUI = prefab.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(c => c != null && c.GetType().Name == "ResultUI");
            Assert.That(resultUI, Is.Not.Null, "ResultUI");

            SerializedProperty sprites = new SerializedObject(resultUI).FindProperty("rankStampSprites");
            Assert.That(sprites, Is.Not.Null, "rankStampSprites");

            string[] actual = Enumerable.Range(0, sprites.arraySize)
                .Select(i => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sprites.GetArrayElementAtIndex(i).objectReferenceValue)))
                .ToArray();
            Assert.That(actual, Is.EqualTo(expected));
        }

        // 이름 목록이 값 0부터 빈틈없이 이 순서여야 한다. 이름·값을 바꾸거나 멤버를 더해도 실패
        private static void AssertOrder<TEnum>(params string[] expected) where TEnum : struct, Enum
        {
            Type type = typeof(TEnum);
            Assert.That(Enum.GetNames(type), Is.EqualTo(expected), type.Name);
            for (int i = 0; i < expected.Length; i++)
                Assert.That(Convert.ToInt32(Enum.Parse(type, expected[i])), Is.EqualTo(i), $"{type.Name}.{expected[i]}");
        }

        // 씬 YAML에서 m_Script guid가 scriptGuid인 MonoBehaviour 문서 하나를 찾아 field 배열 원소의 guid를 순서대로 모은다.
        // 비어 있는 원소({fileID: 0})는 빈 문자열
        private static List<string> SceneArrayGuids(string scenePath, string scriptGuid, string field)
        {
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "..", scenePath));

            int[] scriptLines = lines.Select((line, i) => (line, i))
                .Where(x => x.line.StartsWith("  m_Script:") && x.line.Contains("guid: " + scriptGuid))
                .Select(x => x.i).ToArray();
            Assert.That(scriptLines, Has.Length.EqualTo(1), $"{scenePath}: script {scriptGuid}");

            var guids = new List<string>();
            bool inArray = false;
            for (int i = scriptLines[0] + 1; i < lines.Length && !lines[i].StartsWith("--- !u!"); i++)
            {
                string line = lines[i];
                if (inArray)
                {
                    if (!line.StartsWith("  - ")) break;
                    Match m = GuidPattern.Match(line);
                    guids.Add(m.Success ? m.Groups[1].Value : "");
                }
                else if (line.StartsWith("  " + field + ":"))
                {
                    if (line.TrimEnd().EndsWith("[]")) return guids;
                    inArray = true;
                }
            }
            Assert.That(inArray, Is.True, $"{scenePath}: {field}");
            return guids;
        }

        private static string[] FileNames(IEnumerable<string> guids)
            => guids.Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))).ToArray();
    }
}
