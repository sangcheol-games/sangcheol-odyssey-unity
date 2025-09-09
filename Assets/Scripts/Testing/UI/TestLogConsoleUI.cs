using System.Linq;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.Testing
{
    public sealed class TestLogConsoleUI : MonoBehaviour
    {
        public TMP_Text Text;
        public Button ClearBtn;
        public int MaxLines = 200;

        private RingBufferSink _ring;
        private CoreLogger _log;
        private bool _subscribed;

        void Awake()
        {
            if (ClearBtn) ClearBtn.onClick.AddListener(Clear);
        }

        void OnEnable()
        {
            TryInit();
        }

        void Update()
        {
            if (_ring == null) TryInit();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void TryInit()
        {
            if (_ring != null) return;
            if (_log == null) ServiceLocator.TryGet(out _log);
            if (_log == null) return;
            if (!ServiceLocator.TryGet(out _ring) || _ring == null)
            {
                _ring = new RingBufferSink(MaxLines);
                _log.AddSink(_ring);
            }
            if (!_subscribed)
            {
                _ring.OnChanged += Refresh;
                _subscribed = true;
            }
            Refresh();
        }

        void Refresh()
        {
            if (Text == null || _ring == null) return;
            var lines = _ring.Snapshot().Reverse().Take(MaxLines);
            Text.text = string.Join("\n", lines);
        }

        void Clear()
        {
            ServiceLocator.TryGet(out CoreLogger l);
            l?.Info("clear", "ui");
            ServiceLocator.TryGet(out RingBufferSink r);
            if (r == null) return;
            var dummy = new RingBufferSink(MaxLines);
            ServiceLocator.Register(dummy);
            _ring = dummy;
            Refresh();
        }

        void Unsubscribe()
        {
            if (_ring != null && _subscribed)
            {
                _ring.OnChanged -= Refresh;
                _subscribed = false;
            }
        }
    }
}
