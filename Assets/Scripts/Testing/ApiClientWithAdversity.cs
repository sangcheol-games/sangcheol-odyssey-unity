using System;
using System.Threading.Tasks;
using SCOdyssey.Net;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.Testing
{
    public sealed class ApiClientWithAdversity : IApiClient
    {
        private readonly IApiClient _inner;
        private readonly BackoffPolicy _backoff;

        public ApiClientWithAdversity(IApiClient inner, TestingConfig cfg)
        {
            _inner = inner;
            _backoff = new BackoffPolicy(cfg.backoffInitialMs, cfg.backoffMaxMs);
        }

        private async Task<T> InvokeWithAdversity<T>(Func<Task<T>> call)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    await NetworkConditions.ApplyAsync();
                    return await call();
                }
                catch (NetworkConditions.ArtificialDropException)
                {
                    await _backoff.WaitAsync(attempt++);
                }
            }
        }

        private async Task InvokeWithAdversity(Func<Task> call)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    await NetworkConditions.ApplyAsync();
                    await call();
                    return;
                }
                catch (NetworkConditions.ArtificialDropException)
                {
                    await _backoff.WaitAsync(attempt++);
                }
            }
        }

        public Task<AuthUrlResponse> SessionInit(string codeVerifier)
            => InvokeWithAdversity(() => _inner.SessionInit(codeVerifier));

        public Task<AuthTokens> SessionPoll(string sessionId)
            => InvokeWithAdversity(() => _inner.SessionPoll(sessionId));

        public Task Logout()
            => InvokeWithAdversity(() => _inner.Logout());

        public Task<AuthTokens> Refresh(string refreshToken)
            => InvokeWithAdversity(() => _inner.Refresh(refreshToken));

        public Task<string> Ping()
            => InvokeWithAdversity(() => _inner.Ping());

        public Task<UserMe> GetMe(string accessToken = null)
            => InvokeWithAdversity(() => _inner.GetMe(accessToken));

        public Task<UserMe> SetNickname(string nickname)
            => InvokeWithAdversity(() => _inner.SetNickname(nickname));

        public Task<LinkOut> LinkIdentity(string provider, string sub, object claims = null)
            => InvokeWithAdversity(() => _inner.LinkIdentity(provider, sub, claims));

        public Task<UnlinkOut> UnlinkIdentity(string provider)
            => InvokeWithAdversity(() => _inner.UnlinkIdentity(provider));
    }
}
