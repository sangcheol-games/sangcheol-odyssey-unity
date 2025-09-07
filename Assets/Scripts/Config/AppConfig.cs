using SCOdyssey.Core.Logging;
using UnityEngine;

namespace SCOdyssey.Config
{
    [CreateAssetMenu(fileName = "AppConfig", menuName = "SCOdyssey/AppConfig")]
    public sealed class AppConfig : ScriptableObject
    {
        [Header("Server")]
        public string baseUrl = "http://localhost:5000/v1";
        public bool useMockApi = false;

        [Header("Network Test Conditions (optional)")]
        public int backoffInitialMs = 100;
        public int backoffMaxMs = 3000;
        public int minLatencyMs = 50;
        public int maxLatencyMs = 250;
        public float dropRate = 0f;

        [Header("Offline Queue (optional)")]
        public bool enableOfflineQueue = false;
        public int offlineMaxAttempts = 5;
        public int offlineRetryIntervalMs = 1000;

        [Header("Logging")]
        public LogLevel minimumLogLevel =
#if UNITY_EDITOR
            LogLevel.Debug;
#else
            SCOdyssey.Core.Logging.LogLevel.Info;
#endif
        public int ringCapacity = 512;
        public string logFileName = "app.log";
        public int rollBytes = 5_000_000;
    }
}
