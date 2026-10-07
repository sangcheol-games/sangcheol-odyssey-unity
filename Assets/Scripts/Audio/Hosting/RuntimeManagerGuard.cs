using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // FMODUnity.RuntimeManager를 참조하는 유일한 곳(초기화 여부 읽기 전용, ChartEditor 제외).
    // 게임 경로에서 RuntimeManager가 초기화되면 FMOD System이 두 개가 되므로 오류로 알린다.
    public static class RuntimeManagerGuard
    {
        public static bool Enabled { get; set; }

        public static bool IsInitialized
        {
            get { return FMODUnity.RuntimeManager.IsInitialized; }
        }

        public static void Check(string when)
        {
            if (!Enabled) return;
            if (IsInitialized) Debug.LogError("[Audio] RuntimeManager가 초기화되어 있습니다(" + when + "). 게임 경로에서 RuntimeManager를 쓰는 코드를 찾아야 합니다.");
        }
    }
}
