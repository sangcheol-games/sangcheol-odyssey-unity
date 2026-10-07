using SCOdyssey.Game.Timing.Judgement;
using SCOdyssey.Game.Timing.LaneInput;
using UnityEngine;

namespace SCOdyssey.Game.Timing.Diagnostics
{
    // 타이밍 오버레이(에디터·개발 빌드). JudgementDriver의 OnGUI에서 오디오 오버레이 아래에 그린다.
    internal static class TimingOverlay
    {
        private const float Width = 420f;
        private const float Height = 170f;
        private const float Top = 170f;
        private const int RecentSamples = 64;

        public static void Draw(JudgementDriver driver)
        {
            if (!Debug.isDebugBuild) return;

            string latch = "래치 전";
            if (driver.IsJudgmentOffsetLatched) latch = driver.LatchedJudgmentOffsetSteps + "단계";

            GUILayout.BeginArea(new Rect(Screen.width - Width - 10f, Top, Width, Height), GUI.skin.box);
            GUILayout.Label("판정: 클라이언트 " + driver.IsAttached + ", 판정 싱크 " + latch);
            GUILayout.Label(string.Format("입력: 이번 배치 {0}, 전달 {1}, 거부 {2}, 프레임 시각으로 자름 {3}",
                driver.LastBatchCount, driver.DeliveredInputs, driver.RejectedInputs, driver.ClampedInputs));

            var unitySource = driver.Source as UnityInputSystemTimestampSource;
            if (unitySource != null)
            {
                GUILayout.Label(string.Format("소스: Push {0}, Synthetic {1}, 버림 {2}, 매퍼 오프셋 {3:F6}s",
                    unitySource.PushedCount, unitySource.SyntheticCount, unitySource.DroppedCount, unitySource.MapperOffsetSeconds));
            }

            TimingLog log = driver.TimingLog;
            DrawStats("Press", log.Summarize(TimingKind.Press, RecentSamples));
            DrawStats("Release", log.Summarize(TimingKind.Release, RecentSamples));
            GUILayout.EndArea();
        }

        private static void DrawStats(string label, TimingStats stats)
        {
            if (stats.Count == 0)
            {
                GUILayout.Label(label + " 오차: 기록 없음");
                return;
            }
            GUILayout.Label(string.Format("{0} 오차(최근 {1}건): 평균 {2:+0.0;-0.0;0.0}ms, 표준편차 {3:F1}ms", label, stats.Count, stats.MeanMs, stats.StdDevMs));
        }
    }
}
