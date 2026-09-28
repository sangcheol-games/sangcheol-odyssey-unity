using SCOdyssey.Audio.Hosting;
using UnityEngine;

namespace SCOdyssey.Audio.Diagnostics
{
    // 오디오 상태 오버레이(에디터·개발 빌드). AudioEngineRunner의 OnGUI에서 그린다.
    public static class AudioOverlay
    {
        private const float Width = 420f;
        private const float Height = 150f;

        public static bool Visible { get; set; }

        internal static void Draw(AudioModule module)
        {
            if (!Debug.isDebugBuild) return;

            IAudioEngine engine = module.Engine;
            AudioOutputInfo output = engine.CurrentOutput;
            double blockMs = 0;
            if (output.SampleRate > 0) blockMs = output.BufferLength * 1000.0 / output.SampleRate;

            GUILayout.BeginArea(new Rect(Screen.width - Width - 10f, 10f, Width, Height), GUI.skin.box);
            GUILayout.Label("오디오: " + engine.Status + ", 세대 " + engine.Generation);
            GUILayout.Label("출력: " + output.Kind + " / " + output.DeviceName);
            GUILayout.Label(string.Format("{0}Hz, 버퍼 {1}x{2} (블록 {3:F2}ms)", output.SampleRate, output.BufferLength, output.BufferCount, blockMs));
            GUILayout.Label("FMOD 오류 콜백: " + module.TotalFmodErrors + ", RM(Studio) 초기화: " + RuntimeManagerGuard.IsInitialized);
            GUILayout.EndArea();
        }
    }
}
