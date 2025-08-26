using System;

namespace SCOdyssey.Domain.Dto
{
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
    public class NicknameBody
    {
        public string nickname;
    }

    [Serializable]
    public class LinkBody
    {
        public string provider_sub;
        public object claims;
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
}
