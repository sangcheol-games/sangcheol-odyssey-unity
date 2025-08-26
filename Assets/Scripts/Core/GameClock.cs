using System;
using UnityEngine;

namespace SCOdyssey.Core
{
    public sealed class GameClock
    {
        private long _serverOffsetMs;
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _serverOffsetMs;
        public void SetServerOffsetMs(long ms) => _serverOffsetMs = ms;
    }
}
