using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SCOdyssey.Audio.Engine
{
    // FMOD API는 전부 메인 스레드에서 부른다. 설치 때 메인 스레드를 기록하고, 에디터·개발 빌드에서 검사한다.
    internal static class AudioThread
    {
        private static int s_mainThreadId = -1;

        public static void CaptureMain()
        {
            s_mainThreadId = Environment.CurrentManagedThreadId;
        }

        public static int MainThreadId
        {
            get { return s_mainThreadId; }
        }

        public static bool IsMain
        {
            get
            {
                if (s_mainThreadId < 0) return true;
                return Environment.CurrentManagedThreadId == s_mainThreadId;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void AssertMain(string api)
        {
            if (!IsMain) Debug.LogError("[Audio] 메인 스레드가 아닌 곳에서 호출했습니다: " + api);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("ole32.dll")]
        private static extern int CoGetApartmentType(out int aptType, out int aptQualifier);

        // ASIO 드라이버는 COM이라 메인 스레드 아파트먼트를 기록해 둔다(SP2: MAINSTA).
        public static string DescribeApartment()
        {
            int type;
            int qualifier;
            int hr;
            try
            {
                hr = CoGetApartmentType(out type, out qualifier);
            }
            catch (Exception e)
            {
                return "조회 실패(" + e.GetType().Name + ")";
            }
            if (hr != 0) return string.Format("COM 미초기화(0x{0:X8})", hr);
            if (type == 0) return "STA";
            if (type == 1) return "MTA";
            if (type == 2) return "NA";
            if (type == 3) return "MAINSTA";
            return "알 수 없음(" + type + ")";
        }
#else
        public static string DescribeApartment()
        {
            return "해당 없음";
        }
#endif
    }
}
