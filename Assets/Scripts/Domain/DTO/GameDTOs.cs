using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class SessionStartReq
    {
        public string chart_id;
        public string seed;
    }
    [Serializable]
    public class GameSession
    {
        public string session_id;
        public long issued_at;
    }
    [Serializable]
    public class SessionResultReq
    {
        public string session_id;
        public int score;
        public string hash;
    }
}
