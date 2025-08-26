using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class AuthTokens
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;   // sec
        public string token_type;   // "bearer"
        public bool is_new_user;
    }

    [Serializable]
    public class UserMe
    {
        public string id;
        public string uid;
        public string nickname;
        public string created_at;
        public string updated_at;
        public string last_login_at;
    }

    [Serializable]
    public class LinkOut
    {
        public string provider;
        public string provider_sub;
    }

    [Serializable]
    public class UnlinkOut
    {
        public bool deleted;
        public string provider;
        public string provider_sub;
    }

    [Serializable]
    public class ApiError
    {
        public string detail;
        public string code;
    }
}
