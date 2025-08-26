using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SCOdyssey.Testing {
    public sealed class BackoffPolicy {
        private readonly int _initialMs;
        private readonly int _maxMs;

        public BackoffPolicy(int initialMs, int maxMs) {
            _initialMs = Mathf.Max(1, initialMs);
            _maxMs = Mathf.Max(_initialMs, maxMs);
        }

        public async Task WaitAsync(int attempt) {
            double delay = _initialMs * Math.Pow(2, Math.Max(0, attempt));
            if (delay > _maxMs) delay = _maxMs;

            double until = Time.realtimeSinceStartupAsDouble + (delay / 1000.0);
            while (Time.realtimeSinceStartupAsDouble < until) await Task.Yield();
        }
    }
}
