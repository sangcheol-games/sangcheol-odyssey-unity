using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    public struct AutoplayOptions
    {
        public double OffsetSec;         // 모든 입력을 이만큼 늦춘다(음수면 이르게)
        public double TapHoldSec;        // 탭을 누르고 있는 시간
        public double BreakHoldChance;   // 홀드를 중간(30~70% 지점)에 뗄 확률
        public double DropTapChance;     // 탭을 아예 안 칠 확률
        public int Seed;

        public static AutoplayOptions Perfect => new() { TapHoldSec = 0.03 };
    }

    // 판정 트랙에서 입력 스크립트를 만든다. 머리는 머리 시각에 누르고 꼬리 시각에 뗀다.
    // 탭은 같은 레인의 다음 누름보다 늦게 떼지 않는다.
    public static class Autoplay
    {
        private const double MinGapSec = 0.001;

        public static InputScript Perfect(JudgeNote[] track) => Build(track, AutoplayOptions.Perfect);

        public static InputScript Build(JudgeNote[] track, AutoplayOptions options)
        {
            var rng = new Random(options.Seed);
            var script = new InputScript();
            double[] nextPress = NextPressTimes(track);

            for (int i = 0; i < track.Length; i++)
            {
                JudgeNote note = track[i];
                double tapRelease = Math.Min(note.Time + options.TapHoldSec, Math.Max(note.Time, nextPress[i] - MinGapSec));

                switch (note.Kind)
                {
                    case NoteKind.Tap:
                        if (rng.NextDouble() < options.DropTapChance) break;
                        script.Press(note.Lane, note.Time);
                        script.Release(note.Lane, tapRelease);
                        break;

                    case NoteKind.HoldHead:
                        script.Press(note.Lane, note.Time);
                        bool breakHold = rng.NextDouble() < options.BreakHoldChance;
                        if (note.PairId < 0)
                        {
                            script.Release(note.Lane, tapRelease);   // 꼬리 없는 머리는 탭처럼 판정된다
                            break;
                        }

                        double end = track[note.PairId].Time;
                        script.Release(note.Lane, breakHold ? note.Time + (end - note.Time) * (0.3 + 0.4 * rng.NextDouble()) : end);
                        break;
                }
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
                if (track[i].Kind != NoteKind.HoldTail) next[lane] = track[i].Time;
            }
            return result;
        }
    }
}
