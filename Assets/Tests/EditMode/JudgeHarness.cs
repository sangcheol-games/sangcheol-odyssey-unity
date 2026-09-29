using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    public readonly struct ScriptedInput
    {
        public readonly double Time;
        public readonly Lane Lane;
        public readonly bool IsPress;

        public ScriptedInput(double time, Lane lane, bool isPress)
        {
            Time = time;
            Lane = lane;
            IsPress = isPress;
        }

        public static ScriptedInput Press(double time, Lane lane) => new(time, lane, true);
        public static ScriptedInput Release(double time, Lane lane) => new(time, lane, false);
    }

    // JudgeEngine을 게임과 같은 순서(그 프레임까지의 입력 먼저, 그 다음 Advance)로 몰고 판정 이벤트를 모은다.
    public sealed class JudgeHarness
    {
        public readonly JudgeEngine Engine;
        public readonly List<JudgeEvent> Events = new();

        public JudgeHarness(JudgeNote[] notes, double offsetSec = 0)
            : this(notes, JudgeSettings.Default.WithOffset(offsetSec)) {}

        public JudgeHarness(JudgeNote[] notes, JudgeSettings settings)
        {
            Engine = new JudgeEngine(settings);
            Engine.Load(notes);
        }

        public void Advance(double time) => Engine.Advance(time, Events);

        public bool Press(Lane lane, double time) => Engine.Press(lane, time, Events);

        public bool Release(Lane lane, double time) => Engine.Release(lane, time, Events);

        public void Run(IEnumerable<ScriptedInput> inputs, double stepSec, double endSec)
        {
            List<ScriptedInput> ordered = inputs.OrderBy(i => i.Time).ToList();
            int next = 0;

            for (int frame = 0; ; frame++)
            {
                double now = Math.Min(frame * stepSec, endSec);

                while (next < ordered.Count && ordered[next].Time <= now)
                {
                    ScriptedInput input = ordered[next++];
                    if (input.IsPress) Press(input.Lane, input.Time);
                    else Release(input.Lane, input.Time);
                }

                Advance(now);
                if (now >= endSec) break;
            }
        }

        public JudgeEvent Single(int noteId)
        {
            List<JudgeEvent> matches = Events.Where(e => e.NoteId == noteId).ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"note #{noteId}: {string.Join(", ", matches)}");
            return matches[0];
        }

        // noteId 순. 틱 간격과 무관하게 같아야 하는 결과를 비교할 때 쓴다
        public string[] Outcomes()
            => Events.OrderBy(e => e.NoteId).Select(Describe).ToArray();

        public static string Describe(JudgeEvent e)
            => $"#{e.NoteId} {e.Kind} {(e.IsMiss ? "Miss" : e.Judge.ToString())}";
    }
}
