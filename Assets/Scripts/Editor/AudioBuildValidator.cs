using System.Collections.Generic;
using System.IO;
using SCOdyssey.Domain.Entity;
using SCOdyssey.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SCOdyssey.EditorTools
{
    // 빌드 전에 게임이 쓰는 음원 파일이 StreamingAssets에 실제로 있는지 확인한다(파일명은 바꾸지 않는다).
    //   - MusicSO의 곡·프리뷰 음원(StreamingAssets/Music)
    //   - MainUI 프리팹의 로비 BGM(StreamingAssets/Music)
    //   - HitSoundPlayer의 타격음(StreamingAssets/HitSound)
    // 빠진 파일이 있으면 빌드를 멈춘다. Force Single Instance가 꺼져 있으면 경고만 한다(두 번 실행하면 오디오 장치를 서로 잡는다).
    public sealed class AudioBuildValidator : IPreprocessBuildWithReport
    {
        private const string MainUIPrefab = "Assets/Resources/Prefabs/UI/MainUI.prefab";

        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var missing = new List<string>();
            string music = Path.Combine(Application.streamingAssetsPath, "Music");
            string hitSound = Path.Combine(Application.streamingAssetsPath, "HitSound");

            CheckMusicAssets(music, missing);
            CheckLobbyBgm(music, missing);
            CheckHitSounds(hitSound, missing);

            if (!PlayerSettings.forceSingleInstance)
            {
                Debug.LogWarning("[AudioBuildValidator] Player Settings의 Force Single Instance가 꺼져 있습니다. 게임을 두 번 실행하면 오디오 장치를 서로 잡습니다.");
            }

            if (missing.Count > 0)
            {
                throw new BuildFailedException("[AudioBuildValidator] 음원 파일이 없습니다:\n" + string.Join("\n", missing));
            }
            Debug.Log("[AudioBuildValidator] 음원 파일 확인 완료");
        }

        private static void CheckMusicAssets(string folder, List<string> missing)
        {
            string[] guids = AssetDatabase.FindAssets("t:MusicSO");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MusicSO music = AssetDatabase.LoadAssetAtPath<MusicSO>(path);
                if (music == null) continue;
                CheckFile(folder, music.audioFilePath, path + " 곡 음원", missing, true);
                CheckFile(folder, music.previewAudioFilePath, path + " 프리뷰 음원", missing, false);
            }
        }

        private static void CheckLobbyBgm(string folder, List<string> missing)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MainUIPrefab);
            MainUI mainUI = null;
            if (prefab != null) mainUI = prefab.GetComponentInChildren<MainUI>(true);
            if (mainUI == null)
            {
                Debug.LogWarning("[AudioBuildValidator] MainUI 프리팹을 찾지 못해 로비 BGM을 확인하지 않았습니다: " + MainUIPrefab);
                return;
            }
            SerializedProperty property = new SerializedObject(mainUI).FindProperty("bgmFileName");
            if (property == null)
            {
                Debug.LogWarning("[AudioBuildValidator] MainUI.bgmFileName을 찾지 못해 로비 BGM을 확인하지 않았습니다.");
                return;
            }
            CheckFile(folder, property.stringValue, "MainUI 로비 BGM", missing, true);
        }

        // 타격음 목록은 HitSoundPlayer가 가진다. 빠진 파일은 missing에 쌓여 빌드를 멈춘다
        private static void CheckHitSounds(string folder, List<string> missing)
        {
            string[] files = HitSoundPlayer.HitSoundFiles;
            for (int i = 0; i < files.Length; i++) CheckFile(folder, files[i], "타격음", missing, true);
        }

        private static void CheckFile(string folder, string fileName, string owner, List<string> missing, bool required)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                if (required) missing.Add(owner + ": 파일명이 비어 있음");
                return;
            }
            string path = Path.Combine(folder, fileName);
            if (!File.Exists(path)) missing.Add(owner + ": " + path);
        }
    }
}
