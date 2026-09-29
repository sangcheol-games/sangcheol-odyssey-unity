using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    public struct AutoplayOptions
    {
        public double OffsetSec;         // 모든 입력을 이만큼 늦춘다(음수면 이르게)
        public double TapHoldSec;        // 탭을 누르고 있는 시간
        public double HoldEndSlackSec;   // 끝점(4)을 지나 이만큼 더 누르고 뗀다. 4는 누르고만 있으면 되는 끝점이다
        public double BreakHoldChance;   // 홀드를 중간(30~70% 지점)에 뗄 확률
        public double DropTapChance;     // 탭을 아예 안 칠 확률
        public int Seed;

        public static AutoplayOptions Perfect => new() { TapHoldSec = 0.03, HoldEndSlackSec = 0.05 };
    }

    // 판정 트랙에서 입력 스크립트를 만든다. 같은 레인의 다음 누름보다 늦게 떼지 않는다.
    public static class Autoplay
    {
        private const double MinGapSec = 0.001;
        private const double TrailingReleaseSec = 0.5;

        public static InputScript Perfect(JudgeNote[] track) => Build(track, AutoplayOptions.Perfect);

        public static InputScript Build(JudgeNote[] track, AutoplayOptions options)
        {
            var rng = new Random(options.Seed);
            var script = new InputScript();
            double[] nextPress = NextPressTimes(track);
            var openHead = new int[LANE_COUNT];
            var broken = new bool[LANE_COUNT];
            Array.Fill(openHead, -1);

            for (int i = 0; i < track.Length; i++)
            {
                JudgeNote note = track[i];
                int lane = (int)note.Lane;
                double releaseLimit = Math.Max(note.Time, nextPress[i] - MinGapSec);

                switch (note.Kind)
                {
                    case NoteType.Normal:
                        if (rng.NextDouble() < options.DropTapChance) break;
                        script.Press(note.Lane, note.Time);
                        script.Release(note.Lane, Math.Min(note.Time + options.TapHoldSec, releaseLimit));
                        break;

                    case NoteType.HoldStart:
                        script.Press(note.Lane, note.Time);
                        openHead[lane] = i;
                        broken[lane] = rng.NextDouble() < options.BreakHoldChance;
                        if (broken[lane])
                        {
                            double end = EndOfHold(track, i);
                            script.Release(note.Lane, note.Time + (end - note.Time) * (0.3 + 0.4 * rng.NextDouble()));
                        }
                        break;

                    case NoteType.HoldEnd:
                    case NoteType.HoldRelease:
                        if (openHead[lane] < 0) break;
                        openHead[lane] = -1;
                        if (broken[lane]) break;
                        double release = note.Kind == NoteType.HoldRelease
                            ? note.Time
                            : Math.Min(note.Time + options.HoldEndSlackSec, releaseLimit);
                        script.Release(note.Lane, release);
                        break;
                }
            }

            double last = track.Length > 0 ? track[^1].Time : 0;
            for (int lane = 0; lane < LANE_COUNT; lane++)
            {
                if (openHead[lane] >= 0 && !broken[lane]) script.Release((Lane)lane, last + TrailingReleaseSec);
            }

            return options.OffsetSec == 0 ? script : script.Shifted(options.OffsetSec);
        }

        // i번 노트 뒤로 같은 레인에서 처음 눌러야 하는 시각. 없으면 +무한대
        private static double[] NextPressTimes(JudgeNote[] track)
        {
            var result = new double[track.Length];
            var next = new double[LANE_COUNT];
            Array.Fill(next, double.PositiveInfinity);

            for (int i = track.Length - 1; i >= 0; i--)
            {
                int lane = (int)track[i].Lane;
                result[i] = next[lane];
                if (track[i].Kind == NoteType.Normal || track[i].Kind == NoteType.HoldStart) next[lane] = track[i].Time;
            }
            return result;
        }

        private static double EndOfHold(JudgeNote[] track, int head)
        {
            for (int j = head + 1; j < track.Length; j++)
            {
                if (track[j].Lane != track[head].Lane) continue;
                if (track[j].Kind == NoteType.HoldEnd || track[j].Kind == NoteType.HoldRelease) return track[j].Time;
            }
            return track[^1].Time + TrailingReleaseSec;
        }
    }
}
