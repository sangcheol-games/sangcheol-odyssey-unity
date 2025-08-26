using UnityEngine;

namespace SCOdyssey.Testing {
    [CreateAssetMenu(fileName = "TestingConfig", menuName = "SCOdyssey/Testing/Config")]
    public sealed class TestingConfig : ScriptableObject {
        [Header("Source")]
        public bool useMockApi = true;
        public string httpBaseUrl = "http://localhost:8000";

        [Header("Adversity")]
        public int baseLatencyMs = 0;
        public int jitterMs = 0;
        [Range(0, 1f)] public float dropRate = 0f;

        [Header("Backoff")]
        public int backoffInitialMs = 500;
        public int backoffMaxMs = 8000;

        [Header("Offline Queue")]
        public bool enableOfflineQueue = true;
        public int offlineRetryIntervalMs = 2000;
        public int offlineMaxAttempts = 10;
    }
}
