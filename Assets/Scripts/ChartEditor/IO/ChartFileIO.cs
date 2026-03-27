using System.IO;
using UnityEngine;

namespace SCOdyssey.ChartEditor.IO
{
    /// <summary>
    /// 채보 파일 읽기/쓰기 및 파일 다이얼로그 처리
    /// </summary>
    public static class ChartFileIO
    {
        // 기본 채보 저장 경로 (Assets/Charts)
        private static string DefaultChartDirectory =>
            Path.Combine(Application.dataPath, "Charts");

        // 기본 음원 경로 (Assets/Audios)
        private static string DefaultAudioDirectory =>
            Path.Combine(Application.dataPath, "Audios");

        /// <summary>
        /// 채보 텍스트를 파일로 저장
        /// </summary>
        public static bool SaveToFile(string path, string content)
        {
            try
            {
                // 디렉토리가 없으면 생성
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, content);
                Debug.Log($"[ChartFileIO] Chart saved to: {path}");
                return true;
            }
            catch (IOException e)
            {
                Debug.LogError($"[ChartFileIO] Save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 파일에서 채보 텍스트 읽기
        /// </summary>
        public static string LoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"[ChartFileIO] File not found: {path}");
                    return null;
                }

                string content = File.ReadAllText(path);
                Debug.Log($"[ChartFileIO] Chart loaded from: {path}");
                return content;
            }
            catch (IOException e)
            {
                Debug.LogError($"[ChartFileIO] Load failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 파일 저장 다이얼로그
        /// </summary>
        public static string ShowSaveDialog(string defaultName = "chart.txt")
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.SaveFilePanel(
                "채보 저장",
                DefaultChartDirectory,
                defaultName,
                "txt"
            );
            return string.IsNullOrEmpty(path) ? null : path;
#else
            EnsureDefaultDirectory();
            string path = SFB.StandaloneFileBrowser.SaveFilePanel(
                "채보 저장",
                DefaultChartDirectory,
                defaultName,
                "txt"
            );
            return string.IsNullOrEmpty(path) ? null : path;
#endif
        }

        /// <summary>
        /// 파일 열기 다이얼로그
        /// </summary>
        public static string ShowOpenDialog()
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel(
                "채보 불러오기",
                DefaultChartDirectory,
                "txt"
            );
            return string.IsNullOrEmpty(path) ? null : path;
#else
            EnsureDefaultDirectory();
            string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel(
                "채보 불러오기",
                DefaultChartDirectory,
                "txt",
                false
            );
            return (paths != null && paths.Length > 0) ? paths[0] : null;
#endif
        }

        /// <summary>
        /// 음원 파일 열기 다이얼로그
        /// </summary>
        public static string ShowAudioOpenDialog()
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel(
                "음원 파일 불러오기",
                DefaultAudioDirectory,
                "wav,mp3,ogg"
            );
            return string.IsNullOrEmpty(path) ? null : path;
#else
            EnsureDefaultDirectory();
            SFB.ExtensionFilter[] filters =
            {
                new SFB.ExtensionFilter("음원 파일", "wav", "mp3", "ogg"),
                new SFB.ExtensionFilter("모든 파일", "*"),
            };
            string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel(
                "음원 파일 불러오기",
                DefaultAudioDirectory,
                filters,
                false
            );
            return (paths != null && paths.Length > 0) ? paths[0] : null;
#endif
        }

        private static void EnsureDefaultDirectory()
        {
            if (!Directory.Exists(DefaultChartDirectory))
                Directory.CreateDirectory(DefaultChartDirectory);
        }
    }
}
