using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SCOdyssey.Testing.Config;
using UnityEngine;

namespace SCOdyssey.Testing.Net
{
    public sealed class TestOfflineQueue : MonoBehaviour
    {
        [SerializeField] private TestingConfig config;

        private readonly LinkedList<ITestOfflineItem> _items = new();
        private bool _running;

        public void Enqueue(ITestOfflineItem item)
        {
            foreach (var it in _items) if (it.IdempotencyKey == item.IdempotencyKey) return;
            _items.AddLast(item);
        }

        private void OnEnable()
        {
            if (_running) return;
            _running = true;
            _ = PumpLoop();
        }

        private void OnDisable()
        {
            _running = false;
        }

        private async Task PumpLoop()
        {
            while (_running)
            {
                if (_items.Count == 0) { await Task.Yield(); continue; }

                var node = _items.First;
                var item = node.Value;
                bool ok = false;
                try { ok = await item.TrySendAsync(); }
                catch { }

                if (ok) _items.Remove(node);
                else
                {
                    item.Attempts++;
                    if (item.Attempts >= (config ? config.offlineMaxAttempts : 3))
                    {
                        Debug.LogWarning($"[TestOfflineQueue] drop: {item.IdempotencyKey}");
                        _items.Remove(node);
                    }
                    else
                    {
                        var ms = (config ? config.offlineRetryIntervalMs : 1000);
                        await DelayRealtime(ms);
                    }
                }
            }
        }

        private static async Task DelayRealtime(int ms)
        {
            double until = Time.realtimeSinceStartupAsDouble + (ms / 1000.0);
            while (Time.realtimeSinceStartupAsDouble < until) await Task.Yield();
        }
    }
}
