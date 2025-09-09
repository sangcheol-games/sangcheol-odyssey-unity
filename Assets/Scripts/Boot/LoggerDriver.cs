using System.Threading;
using UnityEngine;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;

namespace SCOdyssey.Boot
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(ExecutionOrder.Early)]
    public sealed class LoggerDriver : MonoBehaviour
    {
        private CoreLogger _log;

        private static int _suppressUnityForward = 0;
        private int _unityForwarding = 0;

        private void Awake()
        {
            ServiceLocator.TryGet(out _log);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Application.logMessageReceivedThreaded += OnUnityLog;
#endif
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (_log == null && !ServiceLocator.TryGet(out _log)) return;

            Volatile.Write(ref _suppressUnityForward, 1);
            try { _log.Drain(); }
            finally { Volatile.Write(ref _suppressUnityForward, 0); }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Application.logMessageReceivedThreaded -= OnUnityLog;
#endif
            try { _log?.Drain(); } catch { }
        }

        private void OnApplicationQuit()
        {
            try { _log?.Drain(); } catch { }
            try { _log?.Dispose(); } catch { }
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (_log == null) return;
            if (Volatile.Read(ref _suppressUnityForward) == 1) return;
            if (Interlocked.Exchange(ref _unityForwarding, 1) == 1) return;

            try
            {
                var lv = type switch
                {
                    LogType.Error or LogType.Assert or LogType.Exception => LogLevel.Error,
                    LogType.Warning => LogLevel.Warn,
                    _ => LogLevel.Info
                };
                var msg = string.IsNullOrEmpty(stackTrace)
                    ? condition
                    : $"{condition}\n{stackTrace}";
                _log.Write(lv, msg, tag: "unity");
            }
            finally
            {
                Volatile.Write(ref _unityForwarding, 0);
            }
        }
    }
}
