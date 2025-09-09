using System.Collections.Generic;
using UnityEngine;
using SCOdyssey.Boot;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;
using SCOdyssey.Testing.Config;

namespace SCOdyssey.Testing.Boot
{
    public sealed class TestBootInstaller : MonoBehaviour, IInstaller
    {
        public string Id => "test.core";
        public IEnumerable<string> Requires => new[] { "core.logger" };
        public BootPhase Phase => BootPhase.UI;
        public int Priority => 0;
        private TestingConfig config;

        public void Install()
        {
            TestingConfig cfg = null;
            if (!ServiceLocator.TryGet(out cfg) || cfg == null)
                cfg = config;

            if (cfg != null && !ServiceLocator.TryGet(out TestingConfig _))
                ServiceLocator.Register(cfg);

            if (!ServiceLocator.TryGet(out CoreLogger log))
            {
                log = new CoreLogger();
                ServiceLocator.Register(log);
            }

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

            if (!ServiceLocator.TryGet(out IApiClient api))
            {
                var baseUrl = (cfg != null && !string.IsNullOrEmpty(cfg.baseUrl))
                    ? cfg.baseUrl
                    : "http://127.0.0.1:5000/v1";

                api = new HttpApiClient(tok, skew, log);
                ServiceLocator.Register(api);
                log.Info($"IApiClient ready: {api.GetType().Name} ({baseUrl})", "boot");
            }
        }
    }
}
