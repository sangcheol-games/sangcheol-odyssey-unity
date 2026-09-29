using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
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
            var script = new InputScript();
            foreach (ScriptedInput input in inputs) script.Add(input);
            ScriptedRun.Run(Engine, script, FrameSchedule.Uniform(stepSec, endSec), Events);
        }

        public JudgeEvent Single(int noteId)
        {
            List<JudgeEvent> matches = Events.Where(e => e.NoteId == noteId).ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"note #{noteId}: {string.Join(", ", matches)}");
            return matches[0];
        }

        public string[] Outcomes() => JudgeOutcome.Summarize(Events);

        public static string Describe(JudgeEvent e) => JudgeOutcome.Describe(e);
    }
}
