using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class AuthTokens
    {
        public string access_token;
        public string refresh_token;
        public int    expires_in;   // sec
        public string token_type;   // "bearer"
        public bool   is_new_user;
    }
}
