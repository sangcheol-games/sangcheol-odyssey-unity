using System;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Domain.Dto;
using UnityEngine;

namespace SCOdyssey.Net
{
    public sealed class TokenStore
    {
        private const string Key = "SCOdyssey.TokenStore.v1";
        private readonly CoreLogger _log;
        private AuthTokens _tok;
        private DateTime _issuedAtUtc;

        public event Action OnChanged;

        public TokenStore(CoreLogger log) { _log = log; Load(); }

        public bool HasValidAccessToken()
        {
            if (_tok == null || string.IsNullOrEmpty(_tok.access_token)) return false;
            var age = (DateTime.UtcNow - _issuedAtUtc).TotalSeconds;
            return age < Math.Max(10, _tok.expires_in - 5);
        }

        public string AccessToken => _tok?.access_token;
        public string RefreshToken => _tok?.refresh_token;

        public void Set(AuthTokens t)
        {
            _tok = t; _issuedAtUtc = DateTime.UtcNow;
            Save();
            _log.Info("TokenStore updated", "auth");
            OnChanged?.Invoke();
        }

        public void Clear()
        {
            _tok = null;
            PlayerPrefs.DeleteKey(Key);
            _log.Info("TokenStore cleared", "auth");
            OnChanged?.Invoke();
        }

        private void Save()
        {
            var wrapper = new Persist { tokens = _tok, issuedAt = _issuedAtUtc.ToString("o") };
            PlayerPrefs.SetString(Key, JsonAdapter.ToJson(wrapper));
            PlayerPrefs.Save();
        }

        private void Load()
        {
            var s = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(s)) return;
            try
            {
                var w = JsonAdapter.FromJson<Persist>(s);
                _tok = w.tokens;
                _issuedAtUtc = DateTime.Parse(w.issuedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            catch (Exception e)
            {
                _log?.Warn("TokenStore load failed", "auth", e);
            }
        }

        [Serializable]
        private class Persist
        {
            public AuthTokens tokens; public string issuedAt;
        }
    }
}
