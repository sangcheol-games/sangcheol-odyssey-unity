using UnityEditor;
using UnityEngine;
using TMPro;

public static class ApplyUIPrimaryToScene
{
    [MenuItem("Tools/TMP/Apply UI_Primary To Scene")]
    public static void Run()
    {
        var path = "Assets/Fonts/TMP/Primary/UI_Primary.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (!font) { Debug.LogError("UI_Primary.asset not found: " + path); return; }

        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
        {
            t.font = font;
            EditorUtility.SetDirty(t);
        }
        foreach (var i in Object.FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None))
        {
            if (i.textComponent) { i.textComponent.font = font; EditorUtility.SetDirty(i.textComponent); }
            if (i.placeholder is TMP_Text ph) { ph.font = font; EditorUtility.SetDirty(ph); }
            EditorUtility.SetDirty(i);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Applied UI_Primary to all TMP in scene.");
    }
}
