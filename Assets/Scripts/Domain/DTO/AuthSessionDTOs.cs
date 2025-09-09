using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class InitSessionBody
    {
        public string code_verifier;
    }

    [Serializable]
    public class AuthUrlResponse
    {
        public string auth_url;
        public string session_id;
    }

    [Serializable]
    public class RefreshBody
    {
        public string refresh_token;
    }
}
