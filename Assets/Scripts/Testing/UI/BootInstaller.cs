using UnityEngine;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Net;

namespace SCOdyssey.Testing.UI
{
    public sealed class BootInstaller : MonoBehaviour
    {
        [SerializeField] private string defaultBaseUrl = "http://127.0.0.1:8000";
        public static HttpApiClient Api { get; private set; }
        public static TokenStore TokenStore { get; private set; }
        public static RingBufferSink RingSink { get; private set; }

        void Awake()
        {
            var logger = new CoreLogger { MinimumLevel = LogLevel.Debug };
            logger.AddSink(new UnityConsoleSink());
            RingSink = new RingBufferSink(512);
            logger.AddSink(RingSink);

            var clock = new GameClock();
            var skew = new ServerTimeSkew(clock);

            TokenStore = new TokenStore(logger);

            Api = new HttpApiClient(TokenStore, skew, logger);
            Api.SetBaseUrl(PlayerPrefs.GetString("API.BaseUrl", defaultBaseUrl));

            ServiceLocator.Register(logger);
            ServiceLocator.Register(clock);
            ServiceLocator.Register(TokenStore);
            ServiceLocator.Register(Api);
            ServiceLocator.Register(RingSink);
        }
    }
}
