using System;
using System.Collections.Generic;
using System.Text;

namespace SCOdyssey.Core.Logging
{
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
            sb.Append(Utc.ToString("o")).Append(' ');
            sb.Append('[').Append(Level).Append(']');
            if (!string.IsNullOrEmpty(Tag)) sb.Append('[').Append(Tag).Append(']');
            sb.Append(' ').Append(Message);
            if (Context != null && Context.Count > 0)
            {
                sb.Append(" ctx={");
                bool first = true;
                foreach (var kv in Context)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(kv.Key).Append(':').Append(kv.Value);
                }
                sb.Append('}');
            }
            if (Exception != null) sb.Append(" ex=").Append(Exception);
            return sb.ToString();
        }
    }
}
