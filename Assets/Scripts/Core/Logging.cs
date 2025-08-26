using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SCOdyssey.Core.Logging
{
    public enum LogLevel { Trace = 0, Debug = 1, Info = 2, Warn = 3, Error = 4, Fatal = 5 }

    public interface ILogSink : IDisposable
    {
        void Emit(in LogEvent e);
    }

    public readonly struct LogEvent
    {
        public readonly DateTime Utc;
        public readonly LogLevel Level;
        public readonly string Message;
        public readonly string Tag;
        public readonly Exception Exception;
        public readonly IReadOnlyDictionary<string, object> Context;

        public LogEvent(DateTime utc, LogLevel level, string msg, string tag, Exception ex, IReadOnlyDictionary<string, object> ctx)
        {
            Utc = utc; Level = level; Message = msg; Tag = tag; Exception = ex; Context = ctx;
        }

        public string ToLine()
        {
            var sb = new StringBuilder();
            sb.Append(Utc.ToString("o")).Append(" ");
            sb.Append("[").Append(Level).Append("]");
            if (!string.IsNullOrEmpty(Tag)) sb.Append("[").Append(Tag).Append("]");
            sb.Append(" ").Append(Message);
            if (Context != null && Context.Count > 0)
            {
                sb.Append(" ctx={");
                bool first = true;
                foreach (var kv in Context)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append(kv.Key).Append(":").Append(kv.Value);
                }
                sb.Append("}");
            }
            if (Exception != null) sb.Append(" ex=").Append(Exception);
            return sb.ToString();
        }
    }

    public sealed class CoreLogger : IDisposable
    {
        private readonly List<ILogSink> _sinks = new();
        private readonly ConcurrentQueue<LogEvent> _queue = new();
        private readonly SynchronizationContext _unityCtx;
        private readonly Timer _flushTimer;
        private readonly object _gate = new();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
        public bool UseAsyncDispatch { get; set; } = true;
        public int FlushIntervalMs { get; set; } = 100;

        public CoreLogger()
        {
            _unityCtx = SynchronizationContext.Current;
            _flushTimer = new Timer(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);
        }

        public void AddSink(ILogSink sink)
        {
            lock (_gate) _sinks.Add(sink);
        }

        public void RemoveSink(ILogSink sink)
        {
            lock (_gate) _sinks.Remove(sink);
        }

        public void Trace(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Trace, msg, tag, ex, ctx);
        public void Debug(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Debug, msg, tag, ex, ctx);
        public void Info(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Info, msg, tag, ex, ctx);
        public void Warn(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Warn, msg, tag, ex, ctx);
        public void Error(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Error, msg, tag, ex, ctx);
        public void Fatal(string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
            => Write(LogLevel.Fatal, msg, tag, ex, ctx);

        public void Write(LogLevel level, string msg, string tag = null, Exception ex = null, IReadOnlyDictionary<string, object> ctx = null)
        {
            if (level < MinimumLevel) return;
            var e = new LogEvent(DateTime.UtcNow, level, msg, tag, ex, ctx);
            if (UseAsyncDispatch && _unityCtx != null) _queue.Enqueue(e);
            else Emit(e);
        }

        public void Flush()
        {
            if (!UseAsyncDispatch) return;
            if (_unityCtx != null)
            {
                _unityCtx.Post(_ =>
                {
                    while (_queue.TryDequeue(out var e)) Emit(e);
                }, null);
            }
            else
            {
                while (_queue.TryDequeue(out var e)) Emit(e);
            }
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
            try { Flush(); } catch { }
            _flushTimer?.Dispose();
            lock (_gate)
            {
                foreach (var s in _sinks) { try { s.Dispose(); } catch { } }
                _sinks.Clear();
            }
        }
    }

    public sealed class UnityConsoleSink : ILogSink
    {
        public void Emit(in LogEvent e)
        {
            var line = e.ToLine();
            switch (e.Level)
            {
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(line); break;
                case LogLevel.Warn:
                    Debug.LogWarning(line); break;
                default:
                    Debug.Log(line); break;
            }
        }
        public void Dispose() { }
    }

    public sealed class FileSink : ILogSink
    {
        private readonly string _path;
        private readonly long _rollBytes;
        private StreamWriter _writer;

        public FileSink(string path, long rollBytes = 5_000_000)
        {
            _path = path;
            _rollBytes = rollBytes;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
            _writer.AutoFlush = true;
        }

        public void Emit(in LogEvent e)
        {
            try
            {
                _writer.WriteLine(e.ToLine());
                RollIfNeeded();
            }
            catch { }
        }

        private void RollIfNeeded()
        {
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > _rollBytes)
                {
                    _writer.Dispose();
                    var rolled = _path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                        ? _path.Replace(".log", $"_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log")
                        : _path + $"_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    if (File.Exists(rolled)) File.Delete(rolled);
                    File.Move(_path, rolled);
                    _writer = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8);
                    _writer.AutoFlush = true;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { }
        }
    }

    public sealed class RingBufferSink : ILogSink
    {
        private readonly int _capacity;
        private readonly Queue<string> _buf;
        public event Action OnChanged;

        public RingBufferSink(int capacity = 256)
        {
            _capacity = Mathf.Max(8, capacity);
            _buf = new Queue<string>(_capacity);
        }

        public void Emit(in LogEvent e)
        {
            if (_buf.Count >= _capacity) _buf.Dequeue();
            _buf.Enqueue(e.ToLine());
            OnChanged?.Invoke();
        }

        public IReadOnlyCollection<string> Snapshot()
        {
            return _buf.ToArray();
        }

        public void Dispose() { }
    }
}
