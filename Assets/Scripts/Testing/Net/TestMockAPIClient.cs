using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SCOdyssey.Net;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.Testing.Net
{
    public sealed class TestMockApiClient : IApiClient
    {
        public struct Switches
        {
            public bool sessionInitFails;
            public bool sessionPollFails;
            public bool sessionPollPending;
            public bool sessionPollNotFound;
            public bool sessionPollGoogleError;
            public bool refreshFails;
            public bool meFails;
            public bool nicknameConflict;
            public bool nicknameAlreadySet;
            public bool identitiesFail;
        }

        public Switches switches_;
        private AuthTokens _tok = new AuthTokens { access_token = "acc-0", refresh_token = "ref-0", expires_in = 30 };
        private long _accessIssuedAt = NowSec();
        private string _lastSessionId;
        private bool _sessionReady;
        private bool _nicknameHasBeenSet;

        private static long NowSec() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private bool AccessExpired() => (NowSec() - _accessIssuedAt) >= _tok.expires_in;

        public Task<AuthUrlResponse> SessionInit(string codeVerifier)
        {
            if (switches_.sessionInitFails) throw new Exception("mock: session init failed");
            _lastSessionId = "SID_" + Guid.NewGuid().ToString("N")[..6];
            _sessionReady = true;
            return Task.FromResult(new AuthUrlResponse { auth_url = "https://accounts.google.com/o/oauth2/v2/auth?mock=1", session_id = _lastSessionId });
        }

        public Task<AuthTokens> SessionPoll(string sessionId)
        {
            if (switches_.sessionPollFails) throw new Exception("mock: session poll failed");
            if (switches_.sessionPollNotFound) throw new InvalidOperationException("mock: 404");
            if (switches_.sessionPollGoogleError) throw new ArgumentException("mock: 400 google_error");
            if (switches_.sessionPollPending) throw new ApplicationException("mock: 202 pending");
            if (string.IsNullOrEmpty(sessionId) || sessionId != _lastSessionId) throw new InvalidOperationException("mock: invalid sid");
            if (!_sessionReady) throw new ApplicationException("mock: 202 pending");
            _tok = new AuthTokens { access_token = "acc-" + Guid.NewGuid().ToString("N")[..6], refresh_token = "ref-" + Guid.NewGuid().ToString("N")[..6], expires_in = 30 };
            _accessIssuedAt = NowSec();
            return Task.FromResult(_tok);
        }

        public Task Logout()
        {
            _tok = new AuthTokens { access_token = "", refresh_token = "", expires_in = 0 };
            _accessIssuedAt = NowSec();
            return Task.CompletedTask;
        }

        public Task<AuthTokens> Refresh(string refreshToken)
        {
            if (switches_.refreshFails) throw new Exception("mock: refresh failed");
            _tok = new AuthTokens { access_token = "acc-" + Guid.NewGuid().ToString("N")[..6], refresh_token = "ref-" + Guid.NewGuid().ToString("N")[..6], expires_in = 30 };
            _accessIssuedAt = NowSec();
            return Task.FromResult(_tok);
        }

        public Task<string> Ping()
        {
            if (AccessExpired()) throw new UnauthorizedAccessException("mock: 401");
            return Task.FromResult("pong");
        }

        public Task<UserMe> GetMe(string accessToken = null)
        {
            if (switches_.meFails) throw new Exception("mock: /me failed");
            if (AccessExpired()) throw new UnauthorizedAccessException("mock: 401");
            return Task.FromResult(new UserMe { id = Guid.NewGuid().ToString(), uid = "U001", nickname = _nicknameHasBeenSet ? "tester_set" : "tester" });
        }

        public Task<UserMe> SetNickname(string nickname)
        {
            if (AccessExpired()) throw new UnauthorizedAccessException("mock: 401");
            if (switches_.nicknameAlreadySet || _nicknameHasBeenSet) throw new InvalidOperationException("mock: NICKNAME_ALREADY_SET");
            if (switches_.nicknameConflict || nickname == "taken") throw new InvalidOperationException("mock: 409 duplicate");
            if (string.IsNullOrEmpty(nickname) || nickname.Length < 2 || nickname.Length > 16) throw new InvalidOperationException("mock: 422 length");
            if (!Regex.IsMatch(nickname, @"^[\uAC00-\uD7A3\p{IsCJKUnifiedIdeographs}A-Za-z0-9_\.\-]+$"))
                throw new InvalidOperationException("mock: 422 charset");
            _nicknameHasBeenSet = true;
            return Task.FromResult(new UserMe { id = "id", uid = "U001", nickname = nickname });
        }

        public Task<LinkOut> LinkIdentity(string provider, string sub, object claims = null)
        {
            if (AccessExpired()) throw new UnauthorizedAccessException("mock: 401");
            if (switches_.identitiesFail) throw new Exception("mock: link failed");
            return Task.FromResult(new LinkOut { provider = provider, provider_sub = sub });
        }

        public Task<UnlinkOut> UnlinkIdentity(string provider)
        {
            if (AccessExpired()) throw new UnauthorizedAccessException("mock: 401");
            if (switches_.identitiesFail) throw new Exception("mock: unlink failed");
            return Task.FromResult(new UnlinkOut { deleted = true, provider = provider, provider_sub = "sub" });
        }
    }
}
