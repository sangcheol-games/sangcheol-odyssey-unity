using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using SCOdyssey.Config;

namespace SCOdyssey.Boot
{
    public sealed class LoggerInstaller : MonoBehaviour, IInstaller
    {
        public string Id => "core.logger";
        public IEnumerable<string> Requires => Array.Empty<string>();
        public BootPhase Phase => BootPhase.Core;
        public int Priority => 0;

        [SerializeField]
        private LogLevel minimumLevel =
#if UNITY_EDITOR
            LogLevel.Debug;
#else
            LogLevel.Info;
#endif
        [SerializeField] private int ringCapacity = 512;
        [SerializeField] private string logFileName = "app.log";
        [SerializeField] private int rollBytes = 5_000_000;

        public void Install()
        {
            if (ServiceLocator.TryGet(out AppConfig app))
            {
                minimumLevel = app.minimumLogLevel;
                ringCapacity = app.ringCapacity;
                logFileName = app.logFileName;
                rollBytes = app.rollBytes;
            }
            if (!ServiceLocator.TryGet(out CoreLogger log))
            {
                log = new CoreLogger { MinimumLevel = minimumLevel };
                ServiceLocator.Register(log);
            }

            if (!ServiceLocator.TryGet(out FileSink fileSink))
            {
                var logDir = Path.Combine(Application.persistentDataPath, "logs");
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, logFileName);
                fileSink = new FileSink(path, rollBytes: rollBytes);
                log.AddSink(fileSink);
                ServiceLocator.Register(fileSink);
            }

            if (!ServiceLocator.TryGet(out RingBufferSink ring))
            {
                ring = new RingBufferSink(capacity: ringCapacity);
                log.AddSink(ring);
                ServiceLocator.Register(ring);
            }

            if (!ServiceLocator.TryGet(out UnityConsoleSink console))
            {
                console = new UnityConsoleSink(ev => ev.Tag != "unity");
                log.AddSink(console);
                ServiceLocator.Register(console);
            }
        }
    }
}
