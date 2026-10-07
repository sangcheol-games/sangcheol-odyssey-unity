#if SCO_AUDIO_HARNESS
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SCOdyssey.Testing.AudioHarness
{
    // 하네스 결과를 persistentDataPath/audio_harness/ 아래 요약 파일로 남긴다.
    // 요약 파일 한 줄 = 확인 하나의 결과. 사용자는 이 파일 내용을 그대로 붙여 넣는다.
    public static class HarnessReport
    {
        public enum Result { Pass, Fail, Info }

        public static string Folder
        {
            get { return Path.Combine(Application.persistentDataPath, "audio_harness"); }
        }

        public static string SummaryPath
        {
            get { return Path.Combine(Folder, "audio_harness_summary.txt"); }
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

        public static void Summary(string check, Result result, string criteria, string measured, string context)
        {
            Directory.CreateDirectory(Folder);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd HH:mm:ss}, {1}, {2}, 기준: {3}, 실측: {4}, {5}, 빌드: {6}",
                DateTime.Now, check, ResultText(result), criteria, measured, context, BuildKind);
            File.AppendAllText(SummaryPath, line + Environment.NewLine, Encoding.UTF8);
            Debug.Log("[AudioHarness] " + line);
        }

        public static Result PassIf(bool condition)
        {
            if (condition) return Result.Pass;
            return Result.Fail;
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
