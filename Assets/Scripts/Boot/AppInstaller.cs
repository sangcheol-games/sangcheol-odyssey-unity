using System.Collections.Generic;
using UnityEngine;
using SCOdyssey.Boot;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;
using SCOdyssey.Testing.Config;
using SCOdyssey.Testing.Net;


namespace SCOdyssey.Boot
{
    public sealed class AppInstaller : MonoBehaviour, IInstaller
    {
        public string Id => "app.core";
        public IEnumerable<string> Requires => new[] { "core.logger" };
        public BootPhase Phase => BootPhase.UI;
        public int Priority => 0;

        [SerializeField] private TestingConfig config;

        public void Install()
        {
            TestingConfig cfg = null;
            if (!ServiceLocator.TryGet(out cfg) || cfg == null)
                cfg = config;

            if (cfg != null)
            {
                if (!ServiceLocator.TryGet(out TestingConfig _))
                    ServiceLocator.Register(cfg);
            }

            ServiceLocator.TryGet(out CoreLogger log);

            if (!ServiceLocator.TryGet(out GameClock clock))
            {
                clock = new GameClock();
                ServiceLocator.Register(clock);
            }

            if (!ServiceLocator.TryGet(out ServerTimeSkew skew))
            {
                skew = new ServerTimeSkew(clock);
                ServiceLocator.Register(skew);
            }

            if (!ServiceLocator.TryGet(out TokenStore tok))
            {
                tok = new TokenStore(log);
                ServiceLocator.Register(tok);
            }

            if (!ServiceLocator.TryGet(out IApiClient api) || api == null)
            {
                var baseUrl = (cfg != null && !string.IsNullOrEmpty(cfg.baseUrl))
                    ? cfg.baseUrl
                    : "http://127.0.0.1:5000/v1";

                if (cfg != null && cfg.useMockApi)
                {
                    api = new TestMockApiClient();
                }
                else
                {
                    api = new HttpApiClient(tok, skew, log);
                }

                ServiceLocator.Register(api);
                log?.Info($"IApiClient ready: {api.GetType().Name} ({baseUrl})", "boot");
            }
        }
    }
}
