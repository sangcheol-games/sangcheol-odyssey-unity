using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SCOdyssey.Rhythm.Tests
{
    // 게임이 실제로 띄우는 노트·홀드바·판정 이펙트 프리팹의 결선.
    // 컴포넌트 타입은 Assembly-CSharp과 UnityEngine.UI에 있어서(이 어셈블리에서 참조 불가) 이름으로 찾고 필드는 SerializedObject로 읽는다.
    public class PrefabWiringTests
    {
        private const string NotePrefabPath = "Assets/Prefabs/NotePrefab.prefab";
        private const string HoldBarPrefabPath = "Assets/Prefabs/HoldBarPrefab.prefab";
        private const string EffectPrefabPath = "Assets/Prefabs/EffectPrefab.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static Component Find(GameObject go, string typeName)
        {
            return go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
        }

        private static GameObject Child(GameObject go, string name)
        {
            Transform child = go.transform.Find(name);
            Assert.That(child, Is.Not.Null, go.name + "/" + name);
            return child.gameObject;
        }

        [Test]
        public void NotePrefab_HasNoMissingScripts()
        {
            GameObject note = Load(NotePrefabPath);

            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(note), Is.Zero, "NotePrefab");
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(Child(note, "NoteImage")), Is.Zero, "NoteImage");
        }

        [Test]
        public void NotePrefab_NoteAdapter_WiresFourNoteTypes_WithoutHoldingNote()
        {
            GameObject note = Load(NotePrefabPath);
            Component adapter = Find(note, "NoteAdapter");
            Assert.That(adapter, Is.Not.Null, "NoteAdapter");

            var so = new SerializedObject(adapter);
            foreach (string field in new[] { "normalNote", "holdStartNote", "holdEndNote", "holdReleaseNote" })
            {
                SerializedProperty prop = so.FindProperty(field);
                Assert.That(prop, Is.Not.Null, field);
                Assert.That(prop.objectReferenceValue, Is.Not.Null, field);
            }

            // 본체(3)는 게임에서 띄우지 않는다. 컴포넌트도 필드도 남아 있으면 안 된다
            Assert.That(so.FindProperty("holdingNote"), Is.Null, "holdingNote");
            Assert.That(Find(note, "HoldingNote"), Is.Null, "HoldingNote component");
        }

        [Test]
        public void NotePrefab_NoteImage_HasHitFrames()
        {
            GameObject image = Child(Load(NotePrefabPath), "NoteImage");
            Component anim = Find(image, "NoteHitAnimation");
            Assert.That(anim, Is.Not.Null, "NoteHitAnimation");

            SerializedProperty frames = new SerializedObject(anim).FindProperty("hitFrames");
            Assert.That(frames, Is.Not.Null, "hitFrames");
            Assert.That(frames.arraySize, Is.GreaterThan(0), "hitFrames");
        }

        [Test]
        public void HoldBarPrefab_IsMaskRoot_WithTiledFillChild()
        {
            GameObject bar = Load(HoldBarPrefabPath);

            // 루트는 RectMask2D 뷰포트(아트 없음, 2배 제작 에셋 보정 0.5). HoldStartNote.SetHoldBar가 이 구조를 기대한다
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(bar), Is.Zero, "HoldBarPrefab");
            Assert.That(Find(bar, "RectMask2D"), Is.Not.Null, "RectMask2D");
            Assert.That(Find(bar, "Image"), Is.Null, "root Image");
            Assert.That(bar.transform.localScale.x, Is.EqualTo(0.5f));

            Component fill = null;
            for (int i = 0; i < bar.transform.childCount && fill == null; i++)
                fill = Find(bar.transform.GetChild(i).gameObject, "Image");
            Assert.That(fill, Is.Not.Null, "child Image");

            const int Tiled = 2;    // UnityEngine.UI.Image.Type.Tiled
            Assert.That(new SerializedObject(fill).FindProperty("m_Type").intValue, Is.EqualTo(Tiled), "Image.Type");
        }

        [Test]
        public void EffectPrefab_HasFiveJudgeSprites()
        {
            GameObject effect = Load(EffectPrefabPath);
            Component controller = Find(effect, "EffectController");
            Assert.That(controller, Is.Not.Null, "EffectController");

            SerializedProperty sprites = new SerializedObject(controller).FindProperty("judgeSprites");
            Assert.That(sprites, Is.Not.Null, "judgeSprites");
            Assert.That(sprites.arraySize, Is.EqualTo(5), "Perfect, Master, Ideal, Kind, Umm");
            for (int i = 0; i < sprites.arraySize; i++)
                Assert.That(sprites.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, "judgeSprites[" + i + "]");
        }
    }
}
