using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;

namespace SCOdyssey.Testing.UI
{
    public sealed class ApiTestUI : MonoBehaviour
    {
        [Header("HeaderBar")]
        public TMP_InputField BaseUrlInput;
        public Button SaveBtn, PingBtn;

        [Header("AuthPanel")]
        public Button InitBtn, PollBtn, RefreshBtn, LogoutBtn;
        public TMP_Text SessionIdText, TokenText;

        [Header("UsersPanel")]
        public Button MeBtn, NickBtn;
        public TMP_InputField NickInput;
        public TMP_Text MeText;

        private HttpApiClient _api;
        private CoreLogger _log;
        private TokenStore _tok;
        private string _sid;

        void Start()
        {
            ServiceLocator.TryGet(out _api);
            ServiceLocator.TryGet(out _tok);
            ServiceLocator.TryGet(out _log);

            BaseUrlInput.text = PlayerPrefs.GetString("API.BaseUrl", "http://127.0.0.1:8000");
            SaveBtn.onClick.AddListener(() =>
            {
                var url = BaseUrlInput.text.Trim();
                PlayerPrefs.SetString("API.BaseUrl", url);
                _api.SetBaseUrl(url);
                _log.Info($"BaseUrl set: {url}", "ui");
            });

            PingBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                var pong = await _api.Ping();
                _log.Info($"Ping OK: {pong}", "ui");
            }));

            InitBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                var verifier = Guid.NewGuid().ToString("N");
                var res = await _api.SessionInit(verifier);
                _sid = res.session_id;
                SessionIdText.text = $"sid: {_sid}";
                Application.OpenURL(res.auth_url);
                _log.Info($"Open auth_url: {res.auth_url}", "ui");
            }));

            PollBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                if (string.IsNullOrEmpty(_sid)) { _log.Warn("No session_id", "ui"); return; }
                var tok = await _api.SessionPoll(_sid);
                if (tok == null)
                {
                    TokenText.text = "token: (pending)";
                    _log.Info("Poll: 202 pending", "ui");
                }
                else
                {
                    TokenText.text = $"token: access({tok.expires_in}s)";
                    _log.Info("Poll OK: access stored", "ui");
                }
            }));

            RefreshBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                var rt = _tok.RefreshToken;
                if (string.IsNullOrEmpty(rt)) { _log.Warn("No refresh token", "ui"); return; }
                var nt = await _api.Refresh(rt);
                _tok.Set(nt);
                TokenText.text = $"token: refreshed({nt.expires_in}s)";
                _log.Info("Refresh rotated", "ui");
            }));

            LogoutBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                await _api.Logout();
                TokenText.text = "token: (logged out)";
                _log.Info("Logged out", "ui");
            }));

            MeBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                var me = await _api.GetMe();
                MeText.text = $"me: {JsonAdapter.ToJson(me)}";
                _log.Info($"Me OK: {me?.nickname}", "ui");
            }));

            NickBtn.onClick.AddListener(() => _ = Run(async () =>
            {
                var nick = (NickInput.text ?? "").Trim();
                if (!IsValidNick(nick)) { _log.Warn("Invalid nickname charset", "ui"); return; }
                var me = await _api.SetNickname(nick);
                MeText.text = $"me*: {JsonAdapter.ToJson(me)}";
                _log.Info($"Nickname updated -> {me?.nickname}", "ui");
            }));
        }

        private static bool IsValidNick(string nickname)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                nickname, @"^[\uAC00-\uD7A3\p{IsCJKUnifiedIdeographs}A-Za-z0-9_.-]+$");
        }

        private async Task Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception e) { _log?.Error(e.Message, "ui", e); }
        }
    }
}
