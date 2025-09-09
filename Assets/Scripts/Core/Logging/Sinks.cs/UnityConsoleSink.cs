using System;
using UnityEngine;

namespace SCOdyssey.Core.Logging
{
    public sealed class UnityConsoleSink : ILogSink
    {
        private readonly Func<LogEvent, bool> _filter;

        public UnityConsoleSink(Func<LogEvent, bool> filter = null)
        {
            _filter = filter;
        }

        public void Emit(in LogEvent e)
        {
            if (_filter != null && !_filter(e)) return;

            var line = e.ToLine();
            switch (e.Level)
            {
                case LogLevel.Error:
                case LogLevel.Fatal:   Debug.LogError(line);   break;
                case LogLevel.Warn:    Debug.LogWarning(line); break;
                default:               Debug.Log(line);        break;
            }
        }

        public void Dispose() { }
    }
}
