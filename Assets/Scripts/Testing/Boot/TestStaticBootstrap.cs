using UnityEngine;
using SCOdyssey.Core;

namespace SCOdyssey.Testing.Boot
{
    public static class TestStaticBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var entry = Object.FindFirstObjectByType<TestBootEntryPoint>(FindObjectsInactive.Include);
            if (entry == null) return;

            if (Object.FindFirstObjectByType<SCOdyssey.Boot.BootOrchestrator>(FindObjectsInactive.Include) != null)
                return;

            if (entry.Config != null)
                ServiceLocator.Register(entry.Config);

            var go = new GameObject("Boot[Test]");
            go.SetActive(false); 
            var orch   = go.AddComponent<SCOdyssey.Boot.BootOrchestrator>();
            var logger = go.AddComponent<SCOdyssey.Boot.LoggerInstaller>();
            var app    = go.AddComponent<SCOdyssey.Boot.AppInstaller>();

            orch.UseInstallers(logger, app);
            Object.DontDestroyOnLoad(go);
            go.SetActive(true);
        }
    }
}
