using System.Runtime.InteropServices;

namespace SCOdyssey.Audio.Engine
{
    // ASIO는 x64에서만 노출한다(ARM64 미지원). 에디터에서는 "에디터에서 ASIO 허용"(기본 OFF)이 켜져 있을 때만 쓴다.
    // ASIO는 프로세스당 하나라 에디터에서 켜 두면 다른 앱·에디터 미리듣기와 부딪칠 수 있기 때문이다.
    public static class AsioPolicy
    {
        public const string EditorPrefsKey = "SCOdyssey.Audio.AllowAsioInEditor";

        public static bool IsSupported
        {
            get { return RuntimeInformation.ProcessArchitecture == Architecture.X64; }
        }

        public static bool AllowInEditor
        {
#if UNITY_EDITOR
            get { return UnityEditor.EditorPrefs.GetBool(EditorPrefsKey, false); }
            set { UnityEditor.EditorPrefs.SetBool(EditorPrefsKey, value); }
#else
            get { return false; }
            set { }
#endif
        }

        public static bool IsAllowed
        {
            get
            {
                if (!IsSupported) return false;
#if UNITY_EDITOR
                return AllowInEditor;
#else
                return true;
#endif
            }
        }
    }
}
