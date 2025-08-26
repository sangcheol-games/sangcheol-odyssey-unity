using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.Testing.UI {
    public sealed class NetAdversityPanel : MonoBehaviour {
        [SerializeField] private TestingConfig config;
        [SerializeField] private Slider latency;
        [SerializeField] private Slider jitter;
        [SerializeField] private Slider drop;
        [SerializeField] private Text readout;

        private void Awake() {
            if (config != null) NetworkConditions.UseConfig(config);
        }

        private void OnEnable() {
            if (config == null) return;
            latency.value = config.baseLatencyMs;
            jitter.value = config.jitterMs;
            drop.value = config.dropRate * 100f;

            latency.onValueChanged.AddListener(v => { config.baseLatencyMs = Mathf.RoundToInt(v); UpdateText(); });
            jitter.onValueChanged.AddListener(v => { config.jitterMs = Mathf.RoundToInt(v); UpdateText(); });
            drop.onValueChanged.AddListener(v => { config.dropRate = Mathf.Clamp01(v / 100f); UpdateText(); });

            UpdateText();
        }

        private void UpdateText() {
            if (readout == null || config == null) return;
            readout.text = $"Latency: {config.baseLatencyMs} ms\nJitter: ±{config.jitterMs} ms\nDrop: {(int)(config.dropRate * 100)}%";
        }
    }
}
