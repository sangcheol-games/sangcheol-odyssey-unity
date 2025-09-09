using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.Core.Logging
{
    public sealed class RingBufferSink : ILogSink
    {
        private readonly int _capacity;
        private readonly Queue<string> _buf;
        private readonly object _gate = new();

        public event Action OnChanged;

        public RingBufferSink(int capacity = 256)
        {
            _capacity = Mathf.Max(8, capacity);
            _buf = new Queue<string>(_capacity);
        }

        public void Emit(in LogEvent e)
        {
            lock (_gate)
            {
                if (_buf.Count >= _capacity) _buf.Dequeue();
                _buf.Enqueue(e.ToLine());
            }
            OnChanged?.Invoke();
        }

        public IReadOnlyCollection<string> Snapshot()
        {
            lock (_gate) return _buf.ToArray();
        }

        public void Dispose() { }
    }
}
