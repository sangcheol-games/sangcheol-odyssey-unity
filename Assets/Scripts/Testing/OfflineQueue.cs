using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace SCOdyssey.Testing {
    public interface IOfflineItem {
        string IdempotencyKey { get; }
        int Attempts { get; set; }
        Task<bool> TrySendAsync();
    }

    public sealed class OfflineQueue : MonoBehaviour {
        [SerializeField] private TestingConfig config;

        private readonly LinkedList<IOfflineItem> _items = new();
        private bool _running;

        private void Awake() {
            if (config != null) NetworkConditions.UseConfig(config);
        }

        public void Enqueue(IOfflineItem item) {
            foreach (var it in _items) if (it.IdempotencyKey == item.IdempotencyKey) return;
            _items.AddLast(item);
        }

        private void OnEnable() { if (!_running) { _running = true; _ = PumpLoop(); } }
        private void OnDisable() { _running = false; }

        private async Task PumpLoop() {
            while (_running) {
                if (_items.Count == 0) { await Task.Yield(); continue; }

                var node = _items.First;
                var item = node.Value;
                bool ok = false;
                try { ok = await item.TrySendAsync(); }
                catch { /* swallow & retry */ }

                if (ok) _items.Remove(node);
                else {
                    item.Attempts++;
                    if (item.Attempts >= config.offlineMaxAttempts) {
                        Debug.LogWarning($"[OfflineQueue] drop: {item.IdempotencyKey}");
                        _items.Remove(node);
                    } else {
                        await DelayRealtime(config.offlineRetryIntervalMs);
                    }
                }
            }
        }

        private static async Task DelayRealtime(int ms) {
            double until = Time.realtimeSinceStartupAsDouble + (ms / 1000.0);
            while (Time.realtimeSinceStartupAsDouble < until) await Task.Yield();
        }
    }
}
