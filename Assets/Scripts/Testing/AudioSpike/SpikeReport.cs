#if SCO_AUDIO_HARNESS
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SCOdyssey.Testing.AudioSpike
{
    // 스파이크 결과를 persistentDataPath/audio_spike/ 아래 요약 파일과 CSV로 남긴다.
    // 요약 파일 한 줄 = 스파이크 하나의 결과. 사용자는 이 파일 내용을 그대로 붙여 넣는다.
    public static class SpikeReport
    {
        public static string Folder
        {
            get { return Path.Combine(Application.persistentDataPath, "audio_spike"); }
        }

        public static string SummaryPath
        {
            get { return Path.Combine(Folder, "audio_spike_summary.txt"); }
        }

        public static string BuildKind
        {
            get
            {
                if (Application.isEditor) return "Editor";
                if (Debug.isDebugBuild) return "Dev";
                return "Release";
            }
        }

        public enum Result { Pass, Fail, Info }

        public static void Summary(string spike, Result result, string criteria, string measured, string context)
        {
            Directory.CreateDirectory(Folder);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}, {1}, {2}, 기준: {3}, 실측: {4}, {5}, 빌드: {6}",
                DateTime.Now, spike, ResultText(result), criteria, measured, context, BuildKind);
            File.AppendAllText(SummaryPath, line + Environment.NewLine, Encoding.UTF8);
            Debug.Log("[AudioSpike] " + line);
        }

        public static Result PassIf(bool condition)
        {
            if (condition) return Result.Pass;
            return Result.Fail;
        }

        public static StreamWriter OpenCsv(string name, string header)
        {
            Directory.CreateDirectory(Folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(Folder, name + "_" + stamp + ".csv");
            var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine(header);
            Debug.Log("[AudioSpike] CSV: " + path);
            return writer;
        }

        public static string Ms(double seconds)
        {
            return (seconds * 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "ms";
        }

        public static string Num(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string ResultText(Result result)
        {
            if (result == Result.Pass) return "PASS";
            if (result == Result.Fail) return "FAIL";
            return "INFO";
        }
    }
}
#endif
