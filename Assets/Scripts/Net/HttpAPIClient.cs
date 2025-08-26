using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using SCOdyssey.Core.Logging;

namespace SCOdyssey.Net
{
    public sealed class HttpApiClient : IApiClient
    {
        private readonly TokenStore _token;
        private readonly ServerTimeSkew _skew;
        private readonly CoreLogger _log;

        private string _baseUrl = "http://127.0.0.1:8000";
        private int _timeoutMs = 15000;
        private int _maxBackoffMs = 8000;

        public HttpApiClient(TokenStore token, ServerTimeSkew skew, CoreLogger log)
        {
            _token = token; _skew = skew; _log = log;
        }

        public void SetBaseUrl(string url) => _baseUrl = url?.TrimEnd('/');
        public void SetTimeoutMs(int ms) => _timeoutMs = Mathf.Max(1000, ms);
        public void SetMaxBackoffMs(int ms) => _maxBackoffMs = Mathf.Max(1000, ms);

        // session based google login flow
        public async Task<AuthUrlResponse> SessionInit(string codeVerifier)
        {
            var url = $"{_baseUrl}/v1/auth/session/init";
            var body = new InitSessionBody { code_verifier = codeVerifier };
            return await SendJson<AuthUrlResponse>("POST", url, body, auth: false);
        }

        public async Task<AuthTokens> SessionPoll(string sessionId)
        {
            var url = $"{_baseUrl}/v1/auth/session/poll?sid={UnityWebRequest.EscapeURL(sessionId)}";
            var (status, text, headers) = await SendWithRetry(() => SendRaw("GET", url, null, auth:false, bearer:null));
            if (headers != null && headers.TryGetValue("date", out var date))
                _skew.ApplyFromDateHeader(date);

            if (status == 202) return null;
            if (status == 200)
            {
                if (string.IsNullOrEmpty(text)) throw new Exception("Empty token response");
                var tok = JsonAdapter.FromJson<AuthTokens>(text);
                _token.Set(new AuthTokens {
                    access_token = tok.access_token,
                    refresh_token = tok.refresh_token,
                    expires_in    = tok.expires_in,
                    token_type    = tok.token_type,
                    is_new_user   = tok.is_new_user
                });
                return tok;
            }
            throw MapHttpError(status, text);
        }

        public async Task Logout()
        {
            var url = $"{_baseUrl}/v1/auth/logout";
            var access = await EnsureAccessTokenAsync();
            var (status, text, _) = await SendWithRetry(() => SendRaw("POST", url, null, auth:true, bearer:access));
            if (status >= 200 && status < 300)
            {
                _token.Clear();
                return;
            }
            throw MapHttpError(status, text);
        }

        // token refresh
        public async Task<AuthTokens> Refresh(string refreshToken)
        {
            var url = $"{_baseUrl}/v1/auth/refresh";
            var body = new RefreshBody { refresh_token = refreshToken };
            return await SendJson<AuthTokens>("POST", url, body, auth:false);
        }

        // general API
        public async Task<string> Ping()
        {
            var url = $"{_baseUrl}/v1/ping/";
            var (status, text, _) = await SendWithRetry(() => SendRaw("GET", url, null, auth: false, bearer: null));
            if (status >= 200 && status < 300) return text;
            throw MapHttpError(status, text);
        }


        // user API
        public async Task<UserMe> GetMe(string accessToken = null)
        {
            var url = $"{_baseUrl}/v1/users/me";
            return await SendJson<UserMe>("GET", url, null, auth: true, overrideAccess: accessToken);
        }

        public async Task<UserMe> SetNickname(string nickname)
        {
            var url = $"{_baseUrl}/v1/users/me/nickname";
            var body = new NicknameBody { nickname = nickname };
            return await SendJson<UserMe>("PATCH", url, body, auth:true);
        }

        public async Task<LinkOut> LinkIdentity(string provider, string sub, object claims = null)
        {
            var url = $"{_baseUrl}/v1/identities/{provider}";
            var body = new LinkBody { provider_sub = sub, claims = claims };
            return await SendJson<LinkOut>("POST", url, body, auth:true);
        }

        public async Task<UnlinkOut> UnlinkIdentity(string provider)
        {
            var url = $"{_baseUrl}/v1/identities/{provider}";
            return await SendJson<UnlinkOut>("DELETE", url, null, auth:true);
        }

        // helpers
        private async Task<T> SendJson<T>(string method, string url, object body, bool auth, string overrideAccess = null)
        {
            string access = null;
            if (auth)
            {
                access = !string.IsNullOrEmpty(overrideAccess) ? overrideAccess : await EnsureAccessTokenAsync();
                if (string.IsNullOrEmpty(access)) throw new Exception("No access token available");
            }

            Func<Task<(long, string, Dictionary<string, string>)>> sender = () => SendRaw(method, url, body, auth, access);

            var (status, text, headers) = await SendWithRetry(sender);
            if (headers != null && headers.TryGetValue("date", out var date)) _skew.ApplyFromDateHeader(date);

            if (status == 401 && auth)
            {
                await TryRefreshOnceAsync();
                (status, text, headers) = await SendWithRetry(sender);
                if (headers != null && headers.TryGetValue("date", out date)) _skew.ApplyFromDateHeader(date);
            }

            if (status < 200 || status >= 300)
            {
                _log.Warn($"HTTP {method} {url} failed status={status} body={text}", "http");
                throw MapHttpError(status, text);
            }

            if (typeof(T) == typeof(object) || string.IsNullOrEmpty(text))
                return default;

            try { return JsonAdapter.FromJson<T>(text); }
            catch (Exception e)
            {
                _log.Error($"JSON parse failed: {e.Message}", "http", e);
                throw;
            }
        }

        private async Task<(long status, string text, Dictionary<string, string> headers)> SendRaw(
            string method, string url, object body, bool auth, string bearer)
        {
            string json = body == null ? null : JsonAdapter.ToJson(body);

            using var req = new UnityWebRequest(url, method);
            if (json != null)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.CeilToInt(_timeoutMs / 1000f);
            if (auth && !string.IsNullOrEmpty(bearer))
                req.SetRequestHeader("Authorization", $"Bearer {bearer}");

            var (code, text, headers) = await SendAsync(req);
            return (code, text, headers);
        }

        private Exception MapHttpError(long status, string text)
        {
            return new Exception($"HTTP {status}: {text}");
        }

        private async Task<(long status, string text, Dictionary<string, string> headers)> SendAsync(UnityWebRequest req)
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            var isErr = req.result != UnityWebRequest.Result.Success;

            var headers = req.GetResponseHeaders() ?? new Dictionary<string, string>();
            var text = req.downloadHandler != null ? req.downloadHandler.text : null;
            var code = (long)req.responseCode;

            if (isErr && code == 0)
                return (0, text, headers);
            return (code, text, headers);
        }

        private async Task<(long, string, Dictionary<string, string>)> SendWithRetry(Func<Task<(long, string, Dictionary<string, string>)>> sender)
        {
            int attempt = 0;
            int delay = 500; // ms
            var rnd = new System.Random();

            while (true)
            {
                attempt++;
                var (status, text, headers) = await sender();

                if ((status >= 200 && status < 300) ||
                    (status >= 400 && status < 500 && status != 429))
                    return (status, text, headers);

                if (status == 0 || status == 429 || (status >= 500 && status <= 599))
                {
                    int jitter = rnd.Next(0, 250);
                    _log.Debug($"retryable status={status}, attempt={attempt}, backoff={delay}+{jitter}ms", "http");
                    await Task.Delay(delay + jitter);
                    delay = Math.Min(delay * 2, _maxBackoffMs);
                    continue;
                }
                return (status, text, headers);
            }
        }

        private async Task<string> EnsureAccessTokenAsync()
        {
            if (_token.HasValidAccessToken())
                return _token.AccessToken;

            await TryRefreshOnceAsync();
            if (_token.HasValidAccessToken())
                return _token.AccessToken;

            throw new Exception("Access token not available after refresh");
        }

        private async Task TryRefreshOnceAsync()
        {
            var rt = _token.RefreshToken;
            if (string.IsNullOrEmpty(rt))
                throw new Exception("No refresh token");

            try
            {
                var tok = await Refresh(rt);
                _token.Set(tok);
                _log.Info("Refresh rotated", "auth");
            }
            catch (Exception e)
            {
                _log.Warn($"Refresh failed: {e.Message}", "auth", e);
                _token.Clear();
                throw;
            }
        }
    }
}
