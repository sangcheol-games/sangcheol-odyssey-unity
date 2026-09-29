using System;
using SCOdyssey.Rhythm;
using UnityEngine;

namespace SCOdyssey.Config
{
    // 판정 규칙 튜닝값. 에셋은 Resources/Config/JudgeSettings. 유저 설정(판정 오프셋)은 ISettingsManager 쪽이다.
    [CreateAssetMenu(fileName = "JudgeSettings", menuName = "SCOdyssey/JudgeSettings")]
    public sealed class JudgeSettingsSO : ScriptableObject
    {
        public const string ResourcePath = "Config/JudgeSettings";

        [Header("판정 윈도우 (ms, 판정 시각 기준 ±)")]
        public float perfectMs = 21f;
        public float masterMs = 42f;
        public float idealMs = 84f;
        public float kindMs = 105f;
        public float ummMs = 126f;

        [Header("노트 선택")]
        public NoteSelectPolicy selectPolicy = NoteSelectPolicy.Earliest;

        // Inspector -> ServiceLocator -> Resources 순으로 찾는다. 없거나 값이 잘못됐으면 코드 기본값
        public static JudgeSettings Resolve(JudgeSettingsSO serialized)
        {
            JudgeSettingsSO so = ConfigLocator.Resolve(serialized, ResourcePath);
            if (so == null) return JudgeSettings.Default;

            try
            {
                return so.ToSettings();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[JudgeSettings] {so.name} 값이 잘못돼 기본값을 쓴다: {e.Message}");
                return JudgeSettings.Default;
            }
        }

        public JudgeSettings ToSettings()
            => new(JudgeWindows.FromMilliseconds(perfectMs, masterMs, idealMs, kindMs, ummMs), selectPolicy);

        private void OnValidate()
        {
            perfectMs = Mathf.Max(0.1f, perfectMs);
            masterMs = Mathf.Max(perfectMs, masterMs);
            idealMs = Mathf.Max(masterMs, idealMs);
            kindMs = Mathf.Max(idealMs, kindMs);
            ummMs = Mathf.Max(kindMs, ummMs);
        }
    }
}
