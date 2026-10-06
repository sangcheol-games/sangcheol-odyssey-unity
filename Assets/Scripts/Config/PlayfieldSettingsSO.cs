using UnityEngine;

namespace SCOdyssey.Config
{
    // 게임 화면 연출 튜닝값. 에셋은 Resources/Config/PlayfieldSettings 하나이고, 노트·판정선 프리팹도 같은 에셋을 읽는다.
    // 마디 박자(4/4)와 음원이 한 마디 늦게 시작하는 규칙은 채보 시각이 기대는 값이라 여기 없다(BarClock, GameManager.StartGame).
    [CreateAssetMenu(fileName = "PlayfieldSettings", menuName = "SCOdyssey/PlayfieldSettings")]
    public sealed class PlayfieldSettingsSO : ScriptableObject
    {
        public const string ResourcePath = "Config/PlayfieldSettings";

        [Header("노트")]
        [Tooltip("유저 설정(노트 투명도)을 못 읽을 때 Ghost 노트의 알파")]
        public float ghostAlphaFallback = 0.2f;
        [Tooltip("Hidden 노트를 판정선이 이만큼(px) 지나간 뒤에 Ghost로 보인다")]
        public float hiddenToGhostOffsetPx = 20f;
        [Tooltip("홀드가 끊긴 뒤 남은 홀드바의 최대 알파")]
        public float brokenHoldAlpha = 0.3f;

        [Header("판정선")]
        [Tooltip("판정선이 화면 가장자리에서 이만큼(px) 더 나가면 풀로 돌아간다")]
        public float timelineScreenMarginPx = 100f;

        [Header("카운트다운")]
        [Tooltip("다음 마디 시작 몇 비트 전부터 숫자를 보여주나")]
        public int countdownBeats = 3;
        [Tooltip("경계에서 첫 숫자가 늦게 뜨지 않게 주는 여유(비트)")]
        public double countdownEpsilonBeats = 0.01;

        [Header("게임 흐름")]
        [Tooltip("클리어 배너를 보여주는 시간(초). 그 뒤 결과창")]
        public float clearBannerSec = 4f;
        [Tooltip("일시정지 해제 카운트다운 시작 숫자")]
        public int resumeCountFrom = 3;
        [Tooltip("일시정지 해제 카운트다운 한 칸(초)")]
        public float resumeCountSec = 1f;

        private static PlayfieldSettingsSO _shared;

        // ServiceLocator -> Resources 순으로 한 번 찾아 둔다. 없으면 코드 기본값으로 만든 인스턴스
        public static PlayfieldSettingsSO Shared
        {
            get
            {
                if (_shared != null) return _shared;

                _shared = ConfigLocator.Resolve<PlayfieldSettingsSO>(null, ResourcePath);
                if (_shared == null)
                {
                    _shared = CreateInstance<PlayfieldSettingsSO>();
                    _shared.hideFlags = HideFlags.DontSave;
                }
                return _shared;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared() => _shared = null;

        private void OnValidate()
        {
            ghostAlphaFallback = Mathf.Clamp01(ghostAlphaFallback);
            brokenHoldAlpha = Mathf.Clamp01(brokenHoldAlpha);
            hiddenToGhostOffsetPx = Mathf.Max(0f, hiddenToGhostOffsetPx);
            timelineScreenMarginPx = Mathf.Max(0f, timelineScreenMarginPx);
            countdownBeats = Mathf.Max(0, countdownBeats);
            countdownEpsilonBeats = System.Math.Max(0, countdownEpsilonBeats);
            clearBannerSec = Mathf.Max(0f, clearBannerSec);
            resumeCountFrom = Mathf.Max(0, resumeCountFrom);
            resumeCountSec = Mathf.Max(0f, resumeCountSec);
        }
    }
}
