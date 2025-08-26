using System;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SCOdyssey.Testing {
    public static class NetworkConditions {
        public static TestingConfig Config { get; private set; }

        public static void UseConfig(TestingConfig cfg) => Config = cfg;

        public static async Task ApplyAsync() {
            if (Config == null) return;

            int waitMs = Config.baseLatencyMs;
            if (Config.jitterMs > 0) {
                waitMs += Random.Range(-Config.jitterMs, Config.jitterMs + 1);
                if (waitMs < 0) waitMs = 0;
            }
            if (waitMs > 0) {
                var until = Time.realtimeSinceStartupAsDouble + (waitMs / 1000.0);
                while (Time.realtimeSinceStartupAsDouble < until) await Task.Yield();
            }

            if (Config.dropRate > 0f && Random.value < Config.dropRate) {
                throw new ArtificialDropException("Testing drop simulated");
            }
        }

        public sealed class ArtificialDropException : Exception {
            public ArtificialDropException(string msg) : base(msg) { }
        }
    }
}
