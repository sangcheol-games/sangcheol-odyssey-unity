using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SCOdyssey.Core.Logging
{
    public sealed class CoreLogger : IDisposable
    {
        private readonly List<ILogSink> _sinks = new();
        private readonly ConcurrentQueue<LogEvent> _queue = new();
        private readonly object _gate = new();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

        public void AddSink(ILogSink sink) { lock (_gate) _sinks.Add(sink); }
        public void RemoveSink(ILogSink sink) { lock (_gate) _sinks.Remove(sink); }

        public void Trace(string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Trace, msg, tag, ex, ctx);
        public void Debug(string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Debug, msg, tag, ex, ctx);
        public void Info (string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Info,  msg, tag, ex, ctx);
        public void Warn (string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Warn,  msg, tag, ex, ctx);
        public void Error(string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Error, msg, tag, ex, ctx);
        public void Fatal(string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null) => Write(LogLevel.Fatal, msg, tag, ex, ctx);

        public void Write(LogLevel level, string msg, string tag=null, Exception ex=null, IReadOnlyDictionary<string, object> ctx=null)
        {
            if (level < MinimumLevel) return;
            _queue.Enqueue(new LogEvent(DateTime.UtcNow, level, msg, tag, ex, ctx));
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var e)) Emit(e);
        }

        private void Emit(in LogEvent e)
        {
            lock (_gate)
            {
                foreach (var s in _sinks)
                {
                    try { s.Emit(e); } catch { }
                }
            }
        }

        public void Dispose()
        {
            try { Drain(); } catch { }
            lock (_gate)
            {
                foreach (var s in _sinks) { try { s.Dispose(); } catch { } }
                _sinks.Clear();
            }
        }
    }
}
