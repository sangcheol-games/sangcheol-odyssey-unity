using System;

namespace SCOdyssey.Core.Logging
{
    public interface ILogSink : IDisposable
    {
        void Emit(in LogEvent e);
    }
}
