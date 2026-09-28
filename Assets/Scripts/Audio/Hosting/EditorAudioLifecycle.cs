using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 에디터에서 플레이 모드 종료·도메인 리로드 직전에 모듈을 종료한다.
    // 리로드 뒤 네이티브 FMOD가 사라진 관리 코드의 콜백을 부르지 않게 하기 위해서다.
    internal static class EditorAudioLifecycle
    {
#if UNITY_EDITOR
        private static AudioModule s_module;

        public static void Register(AudioModule module)
        {
            Unregister(s_module);
            s_module = module;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Unregister(AudioModule module)
        {
            if (module == null || s_module != module) return;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            s_module = null;
        }

        private static void OnBeforeAssemblyReload()
        {
            ShutdownCurrent("도메인 리로드 직전");
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode) ShutdownCurrent("플레이 모드 종료");
        }

        private static void ShutdownCurrent(string reason)
        {
            AudioModule module = s_module;
            if (module == null) return;
            Debug.Log("[Audio] " + reason + ": 모듈을 종료합니다.");
            module.Shutdown();
        }
#else
        public static void Register(AudioModule module)
        {
        }

        public static void Unregister(AudioModule module)
        {
        }
#endif
    }
}
