using System;
using SCOdyssey.Core;

namespace SCOdyssey.Net
{
    public sealed class ServerTimeSkew
    {
        private readonly GameClock _clock;
        public ServerTimeSkew(GameClock clock) { _clock = clock; }

        public void ApplyFromDateHeader(string dateHeader)
        {
            if (string.IsNullOrEmpty(dateHeader)) return;
            if (DateTimeOffset.TryParse(dateHeader, out var server))
            {
                var local = DateTimeOffset.UtcNow;
                var delta = (server - local).TotalMilliseconds;
                _clock.SetServerOffsetMs((long)delta);
            }
        }
    }
}
