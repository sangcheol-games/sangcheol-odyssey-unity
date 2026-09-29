using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class ScriptingTests
    {
        private const int Bpm = 120;

        private static JudgeNote[] Track(params string[] lines) => ChartParser.Parse(string.Join("\n", lines), Bpm).BuildJudgeTrack();

        private static string[] Play(JudgeNote[] track, InputScript script)
        {
            double end = System.Math.Max(track[^1].Time, script.LastTime) + 1.0;
            return JudgeOutcome.Summarize(ScriptedRun.Run(track, JudgeSettings.Default, script, FrameSchedule.Uniform(1.0 / 60, end)));
        }

        [Test]
        public void InputScript_ParsesLinesCommentsAndBlankLines()
        {
            var errors = new List<string>();
            InputScript script = InputScript.Parse("# 머리말\n1.5 1 P\n\n  1.6   1 R   # 뗌\r\n2 4 p\n", errors);

            Assert.That(errors, Is.Empty);
            Assert.That(script.Inputs.Select(i => i.ToString()), Is.EqualTo(new[] { "1.5 1 P", "1.6 1 R", "2 4 P" }));
            Assert.That(script.Inputs[2].Lane, Is.EqualTo(Lane.L4));
        }

        [Test]
        public void InputScript_ReportsBadLines_AndKeepsGoodOnes()
        {
            var errors = new List<string>();
            InputScript script = InputScript.Parse("1.0 5 P\nx 1 P\n1.0 1 X\n1.0 1\n0 1 P\n1.0 0 R", errors);

            Assert.That(errors, Has.Count.EqualTo(5));
            Assert.That(script.Count, Is.EqualTo(1));
        }

        [Test]
        public void InputScript_ToStringRoundTrips()
        {
            InputScript original = new InputScript().Tap(Lane.L2, 1.234375, 0.25).Hold(Lane.L3, 2.0, 2.75).Press(Lane.L4, 0.5);

            InputScript reparsed = InputScript.Parse(original.ToString());

            Assert.That(reparsed.Inputs, Is.EqualTo(original.Inputs));
        }

        [Test]
        public void InputScript_SortsByTime_KeepingInsertionOrderForTies()
        {
            InputScript script = new InputScript()
                .Press(Lane.L1, 2.0)
                .Release(Lane.L1, 1.0)
                .Release(Lane.L2, 1.5)
                .Press(Lane.L2, 1.5);

            Assert.That(script.Inputs.Select(i => i.ToString()), Is.EqualTo(new[] { "1 1 R", "1.5 2 R", "1.5 2 P", "2 1 P" }));
        }

        [Test]
        public void FrameSchedule_Uniform_EndsExactlyAtEnd()
        {
            Assert.That(FrameSchedule.Uniform(0.25, 1.1), Is.EqualTo(new[] { 0, 0.25, 0.5, 0.75, 1.0, 1.1 }));
        }

        [Test]
        public void FrameSchedule_WithHitch_DelaysTheFrameAfterHitchAt()
        {
            Assert.That(FrameSchedule.WithHitch(0.25, 2.0, 0.6, 0.5),
                Is.EqualTo(new[] { 0, 0.25, 0.5, 1.0, 1.25, 1.5, 1.75, 2.0 }));
        }

        [Test]
        public void ManualClock_PauseSpeedAndJump()
        {
            var clock = new ManualClock(1.0);

            clock.Tick(0.5);
            Assert.That(clock.Now, Is.EqualTo(1.5));

            clock.Speed = 2;
            clock.Tick(0.25);
            Assert.That(clock.Now, Is.EqualTo(2.0));

            clock.Paused = true;
            clock.Tick(1.0);
            Assert.That(clock.Now, Is.EqualTo(2.0));

            clock.Jump(0.3);
            clock.Tick(1.0);
            Assert.That(clock.Now, Is.EqualTo(2.3).Within(1e-12), "일시정지 중에도 Jump는 흐른다");
            clock.Tick(1.0);
            Assert.That(clock.Now, Is.EqualTo(2.3).Within(1e-12), "Jump는 한 번만 흐른다");
        }

        [Test]
        public void FrameStepper_MatchesFrameScheduleWithHitch()
        {
            var stepper = new FrameStepper(0, 0.25);
            stepper.ScheduleHitch(0.6, 0.5);
            var frames = new List<double>();

            while (stepper.TryTake(2.0, out double frame)) frames.Add(frame);

            Assert.That(frames, Is.EqualTo(FrameSchedule.WithHitch(0.25, 2.0, 0.6, 0.5)));
        }

        [Test]
        public void FrameStepper_DelayNext_ActsLikeAStall()
        {
            var stepper = new FrameStepper(0, 0.25);
            stepper.TryTake(10, out double first);

            stepper.DelayNext(first, 0.6);

            Assert.That(stepper.TryTake(0.5, out _), Is.False);
            Assert.That(stepper.TryTake(10, out double next), Is.True);
            Assert.That(next, Is.EqualTo(0.6));
        }

        // 샌드박스처럼 들쭉날쭉한 화면 프레임으로 시계를 몰아도 시나리오 결과가 헤드리스와 같다
        [TestCase(1)]
        [TestCase(2)]
        public void FrameStepper_IrregularRenderFrames_MatchHeadlessScenarios(int seed)
        {
            var rng = new System.Random(seed);
            foreach (JudgeScenario scenario in JudgeScenarios.All)
            {
                JudgeNote[] track = scenario.BuildTrack();
                var engine = new JudgeEngine(JudgeSettings.Default);
                engine.Load(track);
                var cursor = new ScriptCursor(scenario.Script);
                var stepper = new FrameStepper(-0.5, JudgeScenario.FrameStepSec);
                if (scenario.HasHitch) stepper.ScheduleHitch(scenario.HitchAt, scenario.HitchSec);
                var events = new List<JudgeEvent>();

                double clock = -0.5, end = scenario.EndTime(track);
                while (engine.Now < end)
                {
                    clock += 0.005 + rng.NextDouble() * 0.1;
                    while (stepper.TryTake(clock, out double frame))
                    {
                        cursor.Feed(engine, frame, events);
                        engine.Advance(frame, events);
                    }
                }

                Assert.That(JudgeOutcome.Summarize(events), Is.EqualTo(scenario.Expected), scenario.Name);
            }
        }

        [Test]
        public void Autoplay_Perfect_TapsAndBothHoldEnds()
        {
            JudgeNote[] track = Track("#001:01:1011;", "#001:02:2004;", "#001:03:2005;", "#001:04:2030;");

            string[] outcomes = Play(track, Autoplay.Perfect(track));

            Assert.That(outcomes, Has.Length.EqualTo(track.Length));
            Assert.That(outcomes, Has.All.EndsWith("Perfect"));
        }

        [Test]
        public void Autoplay_Offset_ShiftsEveryTap()
        {
            JudgeNote[] track = Track("#001:01:1111;");
            AutoplayOptions options = AutoplayOptions.Perfect;
            options.OffsetSec = 0.03;

            Assert.That(Play(track, Autoplay.Build(track, options)), Has.All.EndsWith("Master"));
        }

        [Test]
        public void Autoplay_DropAllTaps_MissesEveryTap()
        {
            JudgeNote[] track = Track("#001:01:1111;");
            AutoplayOptions options = AutoplayOptions.Perfect;
            options.DropTapChance = 1;

            Assert.That(Play(track, Autoplay.Build(track, options)), Has.All.EndsWith("Miss"));
        }

        [Test]
        public void Autoplay_BreakAllHolds_KeepsHeadsButMissesEnds()
        {
            JudgeNote[] track = Track("#001:02:2004;", "#001:03:2005;", "#001:04:2000;");
            AutoplayOptions options = AutoplayOptions.Perfect;
            options.BreakHoldChance = 1;

            string[] outcomes = Play(track, Autoplay.Build(track, options));

            Assert.That(outcomes.Where(o => o.Contains("HoldHead")), Has.All.EndsWith("Perfect"));
            Assert.That(outcomes.Where(o => o.Contains("HoldTail")), Has.All.EndsWith("Miss"));
        }

        [Test]
        public void Autoplay_SameSeed_SameScript()
        {
            JudgeNote[] track = Track("#001:01:1111;", "#001:02:2004;");
            AutoplayOptions options = AutoplayOptions.Perfect;
            options.BreakHoldChance = 0.5;
            options.DropTapChance = 0.5;
            options.Seed = 7;

            Assert.That(Autoplay.Build(track, options).ToString(), Is.EqualTo(Autoplay.Build(track, options).ToString()));
        }

        [TestCase("Chart_0001_Normal", 195, 1.0 / 60)]
        [TestCase("Chart_0001_Hard", 195, 1.0 / 60)]
        [TestCase("Chart_0002_Easy", 155, 1.0 / 60)]
        [TestCase("Chart_0002_Normal", 155, 1.0 / 60)]
        [TestCase("Chart_0002_Hard", 155, 1.0 / 60)]
        [TestCase("Chart_0001_Hard", 195, 0.1)]      // 판정이 프레임과 무관하므로 10fps여도 전부 Perfect
        [TestCase("Chart_0002_Hard", 155, 0.1)]
        public void Autoplay_Perfect_RealCharts_AllPerfect(string name, int bpm, double frameSec)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", name + ".txt"));
            JudgeNote[] track = ChartParser.Parse(text, bpm).BuildJudgeTrack();
            InputScript script = Autoplay.Perfect(track);
            double end = System.Math.Max(track[^1].Time, script.LastTime) + 1.0;

            string[] outcomes = JudgeOutcome.Summarize(ScriptedRun.Run(track, JudgeSettings.Default, script, FrameSchedule.Uniform(frameSec, end)));

            Assert.That(outcomes, Has.Length.EqualTo(track.Length));
            Assert.That(outcomes.Where(o => !o.EndsWith("Perfect")), Is.Empty);
        }
    }
}
