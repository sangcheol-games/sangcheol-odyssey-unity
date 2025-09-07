using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;
using System.Collections.Generic;
using SCOdyssey.Boot;

namespace SCOdyssey.Testing.UI
{
    [DefaultExecutionOrder(ExecutionOrder.Late)]
    public sealed class  TestApiUI: MonoBehaviour
    {
        public Button PingBtn;
        public Button InitBtn, PollBtn, RefreshBtn, LogoutBtn;
        public TMP_Text SessionIdText, TokenText;
        public Button MeBtn, NickBtn;
        public TMP_InputField NickInput;
        public TMP_Text MeText;

        private IApiClient _api;
        private CoreLogger _log;
        private TokenStore _tok;
        private string _sid;

        void Start()
        {
            EnsureDeps();

            if (PingBtn) PingBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                var pong = await _api.Ping();
                _log.Info($"Ping OK: {pong}", "ui");
            }));

            if (InitBtn) InitBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                var res = await _api.SessionInit(Pkce.GenerateCodeVerifier());
                _sid = res.session_id;
                if (SessionIdText) SessionIdText.text = $"sid: {_sid}";
                Application.OpenURL(res.auth_url);
                _log.Info($"Open auth_url: {res.auth_url}", "ui");
            }));

            if (PollBtn) PollBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                if (string.IsNullOrEmpty(_sid)) { _log.Warn("No session_id", "ui"); return; }
                var tok = await _api.SessionPoll(_sid);
                if (tok == null)
                {
                    if (TokenText) TokenText.text = "token: (pending)";
                    _log.Info("Poll: 202 pending", "ui");
                }
                else
                {
                    _tok.Set(tok);
                    if (TokenText) TokenText.text = FormatTokenInfo(tok);
                    _log.Info("Poll OK: access stored", "ui");
                }
            }));

            if (RefreshBtn) RefreshBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                var rt = _tok.RefreshToken;
                if (string.IsNullOrEmpty(rt)) { _log.Warn("No refresh token", "ui"); return; }
                var nt = await _api.Refresh(rt);
                _tok.Set(nt);
                if (TokenText) TokenText.text = FormatTokenInfo(nt);
                _log.Info("Refresh rotated", "ui");
            }));

            if (LogoutBtn) LogoutBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                await _api.Logout();
                if (TokenText) TokenText.text = "token: (logged out)";
                _log.Info("Logged out", "ui");
            }));

            if (MeBtn) MeBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                var me = await _api.GetMe();
                if (MeText) MeText.text = $"me: {JsonAdapter.ToJson(me)}";
                _log.Info($"Me OK: {me?.nickname}", "ui");
            }));

            if (NickBtn) NickBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (!EnsureDeps()) return;
                var nick = (NickInput != null ? NickInput.text : "").Trim();
                if (!IsValidNick(nick)) { _log.Warn("Invalid nickname charset", "ui"); return; }
                var me = await _api.SetNickname(nick);
                if (MeText) MeText.text = $"me*: {JsonAdapter.ToJson(me)}";
                _log.Info($"Nickname updated -> {me?.nickname}", "ui");
            }));
        }

        private bool EnsureDeps()
        {
            if (_api == null && ServiceLocator.TryGet(out IApiClient api)) _api = api;
            if (_tok == null && ServiceLocator.TryGet(out TokenStore tok)) _tok = tok;
            if (_log == null && ServiceLocator.TryGet(out CoreLogger log)) _log = log;

            var missing = new List<string>();
            if (_api == null) missing.Add(nameof(_api));
            if (_tok == null) missing.Add(nameof(_tok));
            if (_log == null) missing.Add(nameof(_log));

            if (missing.Count > 0)
            {
                Debug.unityLogger.LogWarning("ui", $"Deps not ready: {string.Join(", ", missing)}");
                return false;
            }
            return true;
        }

        private static bool IsValidNick(string nickname)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                nickname, @"^[\uAC00-\uD7A3\p{IsCJKUnifiedIdeographs}A-Za-z0-9_.-]+$");
        }

        private async Task Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception e)
            {
                if (_log != null) _log.Error(e.Message, "ui", e);
                else Debug.LogError(e);
            }
        }

        private static string FormatTokenInfo(SCOdyssey.Domain.Dto.AuthTokens tok)
        {
            string Short(string s) => string.IsNullOrEmpty(s) ? "(null)" : s.Substring(0, Mathf.Min(10, s.Length));
            return $"access: {Short(tok.access_token)}..., exp={tok.expires_in}s\n" +
                   $"refresh: {Short(tok.refresh_token)}...";
        }
    }
}
