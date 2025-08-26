using UnityEngine;
using TMPro;
using UnityEngine.UI;
using SCOdyssey.Core;
using SCOdyssey.Core.Logging;

namespace SCOdyssey.Testing.UI
{
    public sealed class LogPanelSink : MonoBehaviour
    {
        [SerializeField] private TMP_Text logText;
        [SerializeField] private ScrollRect scroll;
        private RingBufferSink _ring;

        void OnEnable()
        {
            ServiceLocator.TryGet(out _ring);
            if (_ring != null) _ring.OnChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (_ring != null) _ring.OnChanged -= Refresh;
        }

        public void Clear()
        {
            logText.text = string.Empty;
        }

        private void Refresh()
        {
            if (_ring == null) return;
            var lines = _ring.Snapshot();
            logText.text = string.Join("\n", lines);
            Canvas.ForceUpdateCanvases();
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }
        }
}
