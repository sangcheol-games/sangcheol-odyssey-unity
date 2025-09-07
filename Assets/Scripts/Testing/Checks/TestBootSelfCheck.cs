using UnityEngine;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;

namespace SCOdyssey.Testing.Checks
{
    public sealed class TestBootSelfCheck : MonoBehaviour
    {
        [SerializeField] private bool logOnly = true;

        private void Start()
        {
            var ok = true;

            if (!ServiceLocator.TryGet(out CoreLogger log) || log == null)
            {
                Debug.LogError("[SelfCheck] CoreLogger missing");
                ok = false;
            }
            else
            {
                log.Info("[SelfCheck] CoreLogger OK", "selfcheck");
            }

            if (!ServiceLocator.TryGet(out GameClock clock) || clock == null)
            {
                Debug.LogError("[SelfCheck] GameClock missing");
                ok = false;
            }

            if (!ServiceLocator.TryGet(out ServerTimeSkew skew) || skew == null)
            {
                Debug.LogError("[SelfCheck] ServerTimeSkew missing");
                ok = false;
            }

            if (!ServiceLocator.TryGet(out TokenStore tok) || tok == null)
            {
                Debug.LogError("[SelfCheck] TokenStore missing");
                ok = false;
            }

            if (!ServiceLocator.TryGet(out IApiClient api) || api == null)
            {
                Debug.LogError("[SelfCheck] IApiClient missing");
                ok = false;
            }

            if (ok)
            {
                Debug.Log("[SelfCheck] All core services ready.");
            }
            else if (!logOnly)
            {
                Debug.Break();
            }
        }
    }
}
