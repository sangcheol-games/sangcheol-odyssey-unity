using UnityEngine;
using UnityEngine.SceneManagement;

namespace SCOdyssey.Boot
{
    public static class StaticBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindFirstObjectByType<BootEntryPoint>(FindObjectsInactive.Exclude) == null) return;
            if (Object.FindFirstObjectByType<BootOrchestrator>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("Boot");
            var orchestrator = go.AddComponent<BootOrchestrator>();
            var loggerInstaller = go.AddComponent<LoggerInstaller>();

            var installersField = typeof(BootOrchestrator).GetField("installers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            installersField?.SetValue(orchestrator, new MonoBehaviour[] { loggerInstaller });

            Object.DontDestroyOnLoad(go);
        }
    }
}
