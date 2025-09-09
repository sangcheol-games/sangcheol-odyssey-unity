using System;
using System.Threading.Tasks;
using UnityEngine;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;

namespace SCOdyssey.Testing.Checks
{
    public sealed class TestSmokeRunner : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool tryNicknameUpdate = true;
        [SerializeField] private string nickname = "tester_set";

        private IApiClient _api;
        private CoreLogger _log;
        private TokenStore _tok;
        private string _sid;

        private async void Start()
        {
            if (!runOnStart) return;
            ServiceLocator.TryGet(out _api);
            ServiceLocator.TryGet(out _log);
            ServiceLocator.TryGet(out _tok);

            if (_api == null || _log == null)
            {
                Debug.LogWarning("[Smoke] deps not ready");
                return;
            }

            try
            {
                await DoRun();
            }
            catch (Exception e)
            {
                if (_log != null) _log.Error($"Smoke failed: {e.Message}", "smoke", e);
                else Debug.LogException(e);
            }
        }

        private async Task DoRun()
        {
            try
            {
                var pong = await _api.Ping();
                _log.Info($"[Smoke] Ping OK: {pong}", "smoke");
            }
            catch (Exception e)
            {
                _log.Warn($"[Smoke] Ping failed (ok if 401 pre-auth): {e.Message}", "smoke", e);
            }

            var init = await _api.SessionInit(Pkce.GenerateCodeVerifier());
            _sid = init.session_id;
            _log.Info($"[Smoke] SessionInit OK: sid={_sid}", "smoke");
            
            try
            {
                var t = await _api.SessionPoll(_sid);
                if (t == null)
                {
                    _log.Info("[Smoke] Poll pending (202)", "smoke");
                }
                else
                {
                    _tok.Set(t);
                    _log.Info($"[Smoke] Poll OK: access({t.expires_in}s)", "smoke");
                }
            }
            catch (Exception e)
            {
                _log.Warn($"[Smoke] Poll err: {e.Message}", "smoke", e);
            }

            if (!string.IsNullOrEmpty(_tok?.RefreshToken))
            {
                var nt = await _api.Refresh(_tok.RefreshToken);
                _tok.Set(nt);
                _log.Info($"[Smoke] Refresh OK: access({nt.expires_in}s)", "smoke");
            }

            try
            {
                var me = await _api.GetMe();
                _log.Info($"[Smoke] Me OK: {me?.nickname}", "smoke");
            }
            catch (Exception e)
            {
                _log.Warn($"[Smoke] Me err: {e.Message}", "smoke", e);
            }

            if (tryNicknameUpdate)
            {
                try
                {
                    var me2 = await _api.SetNickname(nickname);
                    _log.Info($"[Smoke] Nickname set -> {me2?.nickname}", "smoke");
                }
                catch (Exception e)
                {
                    _log.Warn($"[Smoke] Nickname err: {e.Message}", "smoke", e);
                }
            }

            _log.Info("[Smoke] done", "smoke");
        }
    }
}
