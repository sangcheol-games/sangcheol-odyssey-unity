using UnityEngine;

namespace SCOdyssey.Testing.Config
{
    [CreateAssetMenu(fileName = "TestingConfig", menuName = "SCOdyssey/Testing/Config")]
    public sealed class TestingConfig : ScriptableObject
    {
        public string baseUrl = "http://localhost:5000/v1";
        public int backoffInitialMs = 100;
        public int backoffMaxMs = 3000;
        public int minLatencyMs = 50;
        public int maxLatencyMs = 250;
        public float dropRate = 0f;

        public int offlineMaxAttempts = 5;
        public int offlineRetryIntervalMs = 1000;

        public bool useMockApi = false;
        public bool enableOfflineQueue = false;
    }
}
