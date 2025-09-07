using System;
using System.IO;
using System.Text;

namespace SCOdyssey.Core.Logging
{
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
            _writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
            { AutoFlush = true };
        }

        public void Emit(in LogEvent e)
        {
            try { _writer.WriteLine(e.ToLine()); RollIfNeeded(); }
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
                    _writer = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
                    { AutoFlush = true };
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { }
        }
    }
}
