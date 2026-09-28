using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    // 지금 JudgeTrack이 하는 일을 그대로 고정한다. 판정 규칙이 바뀌면 여기서 먼저 깨진다.
    public class JudgeTrackTests
    {
        private const double Eps = 1e-9;
        private const double Frame60 = 1.0 / 60;

        private static JudgeNote N(double time, Lane lane, NoteType kind = NoteType.Normal) => new(time, lane, kind);

        private static JudgeHarness Harness(params JudgeNote[] notes) => new(notes);

        private static ScriptedInput Press(double time, Lane lane) => ScriptedInput.Press(time, lane);
        private static ScriptedInput Release(double time, Lane lane) => ScriptedInput.Release(time, lane);

        private static double WindowOf(JudgeType grade) => grade switch
        {
            JudgeType.Perfect => JUDGE_PERFECT,
            JudgeType.Master => JUDGE_MASTER,
            JudgeType.Ideal => JUDGE_IDEAL,
            JudgeType.Kind => JUDGE_KIND,
            _ => JUDGE_UMM,
        };

        private static JudgeType? PressGrade(double noteTime, double pressTime)
        {
            JudgeHarness h = Harness(N(noteTime, Lane.L1));
            return h.Press(Lane.L1, pressTime) ? h.Events[0].Judge : null;
        }

        [TestCase(JudgeType.Perfect, JudgeType.Master)]
        [TestCase(JudgeType.Master, JudgeType.Ideal)]
        [TestCase(JudgeType.Ideal, JudgeType.Kind)]
        [TestCase(JudgeType.Kind, JudgeType.Umm)]
        public void Grade_BoundaryIsInclusive(JudgeType grade, JudgeType next)
        {
            double w = WindowOf(grade);

            Assert.That(PressGrade(0, w), Is.EqualTo(grade), "late");
            Assert.That(PressGrade(0, w + Eps), Is.EqualTo(next), "late + eps");
            Assert.That(PressGrade(w, 0), Is.EqualTo(grade), "early");
            Assert.That(PressGrade(w + Eps, 0), Is.EqualTo(next), "early + eps");
        }

        [Test]
        public void Umm_WindowEdgeIsExclusive()
        {
            double u = JUDGE_UMM;

            Assert.That(PressGrade(0, u), Is.Null, "late by exactly Umm");
            Assert.That(PressGrade(u, 0), Is.Null, "early by exactly Umm");
            Assert.That(PressGrade(0, u - Eps), Is.EqualTo(JudgeType.Umm));
            Assert.That(PressGrade(u - Eps, 0), Is.EqualTo(JudgeType.Umm));
        }

        [Test]
        public void Press_OutsideUmm_NoClaim_ThenSweptAsMiss()
        {
            JudgeHarness h = Harness(N(1.0, Lane.L1));

            Assert.That(h.Press(Lane.L1, 0.8), Is.False);
            Assert.That(h.Press(Lane.L1, 1.2), Is.False);
            Assert.That(h.Events, Is.Empty);

            h.Tick(1.5);
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 Normal Miss" }));
        }

        [Test]
        public void Press_OtherLane_NoClaim()
        {
            JudgeHarness h = Harness(N(0, Lane.L1));

            Assert.That(h.Press(Lane.L2, 0), Is.False);
            Assert.That(h.Press(Lane.L1, 0), Is.True);
        }

        [TestCase(NoteType.Normal, true)]
        [TestCase(NoteType.HoldStart, true)]
        [TestCase(NoteType.Holding, false)]
        [TestCase(NoteType.HoldEnd, false)]
        [TestCase(NoteType.HoldRelease, false)]
        public void Press_ClaimsOnlyNormalAndHoldStart(NoteType kind, bool claimed)
        {
            Assert.That(Harness(N(0, Lane.L1, kind)).Press(Lane.L1, 0), Is.EqualTo(claimed));
        }

        [TestCase(NoteType.Normal, false)]
        [TestCase(NoteType.HoldStart, false)]
        [TestCase(NoteType.Holding, false)]
        [TestCase(NoteType.HoldEnd, false)]
        [TestCase(NoteType.HoldRelease, true)]
        public void Release_ClaimsOnlyHoldRelease(NoteType kind, bool claimed)
        {
            Assert.That(Harness(N(0, Lane.L1, kind)).Release(Lane.L1, 0), Is.EqualTo(claimed));
        }

        [Test]
        public void Earliest_WinsOverlappingWindows()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1), N(0.1, Lane.L1));

            // 두 번째 노트가 더 가깝지만 앞 노트를 집는다
            Assert.That(h.Press(Lane.L1, 0.09), Is.True);
            Assert.That(h.Press(Lane.L1, 0.1), Is.True);

            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#0 Normal Kind", "#1 Normal Perfect" }));
        }

        [Test]
        public void MissSweep_OrderAndTiming()
        {
            JudgeHarness h = Harness(N(0.00, Lane.L1), N(0.05, Lane.L2), N(0.10, Lane.L3), N(0.30, Lane.L4));

            h.Tick(JUDGE_UMM);   // T + Umm 딱 그 시각은 아직 miss가 아니다
            Assert.That(h.Events, Is.Empty);

            h.Tick(0.2);
            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#0 Normal Miss", "#1 Normal Miss" }));
            Assert.That(h.Events.All(e => e.IsMiss && e.Judge == JudgeType.Umm), Is.True);
            Assert.That(h.Track.IsFinished, Is.False);

            h.Tick(1.0);
            Assert.That(h.Events.Select(e => e.NoteId), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(h.Track.IsFinished, Is.True);
        }

        [Test]
        public void MissSweep_SkipsJudgedNotes()
        {
            JudgeHarness h = Harness(N(0.00, Lane.L1), N(0.05, Lane.L2));

            h.Press(Lane.L2, 0.05);
            h.Tick(1.0);

            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#1 Normal Perfect", "#0 Normal Miss" }));
            Assert.That(h.Track.IsFinished, Is.True);
        }

        [Test]
        public void SweptNote_CannotBeClaimed_EvenByPressTimestampedInsideWindow()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1));

            h.Tick(0.2);

            Assert.That(h.Press(Lane.L1, 0.05), Is.False);
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 Normal Miss" }));
        }

        [TestCase(+0.05)]
        [TestCase(-0.05)]
        public void JudgementOffset_ShiftsWindowCenter(double offset)
        {
            var notes = new[] { N(1.0, Lane.L1) };

            var shifted = new JudgeHarness(notes, offset);
            shifted.Press(Lane.L1, 1.0 + offset);
            Assert.That(shifted.Outcomes(), Is.EqualTo(new[] { "#0 Normal Perfect" }));

            var unshifted = new JudgeHarness(notes, offset);
            unshifted.Press(Lane.L1, 1.0);
            Assert.That(unshifted.Outcomes(), Is.EqualTo(new[] { "#0 Normal Ideal" }));

            var sweep = new JudgeHarness(notes, offset);
            sweep.Tick(1.0 + offset + JUDGE_UMM - 0.01);
            Assert.That(sweep.Events, Is.Empty);
            sweep.Tick(1.0 + offset + JUDGE_UMM + 0.01);
            Assert.That(sweep.Outcomes(), Is.EqualTo(new[] { "#0 Normal Miss" }));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Taps_OutcomeIndependentOfTickRate(int seed)
        {
            var rng = new Random(seed);
            var notes = new List<JudgeNote>();
            var inputs = new List<ScriptedInput>();
            double t = 0.5;

            for (int i = 0; i < 200; i++)
            {
                t += 0.03 + rng.NextDouble() * 0.2;
                var lane = (Lane)rng.Next(LANE_COUNT);
                notes.Add(N(t, lane));

                if (rng.NextDouble() < 0.8)
                {
                    double press = t + (rng.NextDouble() - 0.5) * 0.3;   // ±150ms, 일부는 창 밖
                    inputs.Add(Press(press, lane));
                    inputs.Add(Release(press + 0.02, lane));
                }
            }

            double end = t + 1.0;
            var fine = new JudgeHarness(notes.ToArray());
            fine.Run(inputs, 0.001, end);
            var coarse = new JudgeHarness(notes.ToArray());
            coarse.Run(inputs, 0.1, end);

            Assert.That(coarse.Outcomes(), Is.EqualTo(fine.Outcomes()));
            Assert.That(fine.Events, Has.Count.EqualTo(notes.Count));
            Assert.That(fine.Track.IsFinished, Is.True);
        }

        // 홀드 본체(Holding/HoldEnd)는 키를 누르고 있는 동안, Tick이 ±Perfect 창 안에 떨어진 프레임에서만 Perfect가 된다.

        [Test]
        public void HoldBody_PerfectWhileHeld()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1, NoteType.HoldStart), N(0.2, Lane.L1, NoteType.Holding), N(0.4, Lane.L1, NoteType.HoldEnd));

            h.Run(new[] { Press(0.0, Lane.L1), Release(0.5, Lane.L1) }, Frame60, 1.0);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Perfect", "#1 Holding Perfect", "#2 HoldEnd Perfect" }));
        }

        [Test]
        public void HoldBody_FrameStepSkippingPerfectWindow_IsMissed()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1, NoteType.HoldStart), N(0.2, Lane.L1, NoteType.Holding), N(0.4, Lane.L1, NoteType.HoldEnd));

            h.Press(Lane.L1, 0.0);
            h.Tick(0.17);
            h.Tick(0.23);   // 0.2 ± 0.021을 건너뛴 프레임
            h.Tick(0.4);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Perfect", "#1 Holding Miss", "#2 HoldEnd Perfect" }));
        }

        [Test]
        public void HoldBody_ReleasedEarly_IsMissed_OtherLaneKeyDoesNotCount()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1, NoteType.HoldStart), N(0.2, Lane.L1, NoteType.Holding), N(0.4, Lane.L1, NoteType.HoldEnd));

            h.Run(new[] { Press(0.0, Lane.L1), Release(0.1, Lane.L1), Press(0.15, Lane.L2) }, Frame60, 1.0);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Perfect", "#1 Holding Miss", "#2 HoldEnd Miss" }));
        }

        [Test]
        public void HoldBody_KeyHeldAcrossHolds_JudgesNextHoldBodyWithoutItsHead()
        {
            JudgeHarness h = Harness(
                N(0.0, Lane.L1, NoteType.HoldStart), N(0.4, Lane.L1, NoteType.HoldEnd),
                N(0.8, Lane.L1, NoteType.HoldStart), N(1.2, Lane.L1, NoteType.HoldEnd));

            h.Run(new[] { Press(0.0, Lane.L1) }, Frame60, 1.5);

            Assert.That(h.Outcomes(), Is.EqualTo(new[]
            {
                "#0 HoldStart Perfect", "#1 HoldEnd Perfect",
                "#2 HoldStart Miss", "#3 HoldEnd Perfect",
            }));
        }

        [Test]
        public void HoldRelease_JudgedByReleaseTiming_MissedIfKeptHeld()
        {
            var notes = new[] { N(0.0, Lane.L1, NoteType.HoldStart), N(0.4, Lane.L1, NoteType.HoldRelease) };

            var released = new JudgeHarness(notes);
            released.Run(new[] { Press(0.0, Lane.L1), Release(0.43, Lane.L1) }, Frame60, 1.0);
            Assert.That(released.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Perfect", "#1 HoldRelease Master" }));

            var kept = new JudgeHarness(notes);
            kept.Run(new[] { Press(0.0, Lane.L1) }, Frame60, 1.0);
            Assert.That(kept.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Perfect", "#1 HoldRelease Miss" }));
        }

        [Test]
        public void HoldRelease_JudgedEvenIfHeadMissed()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1, NoteType.HoldStart), N(0.4, Lane.L1, NoteType.HoldRelease));

            h.Run(new[] { Press(0.3, Lane.L1), Release(0.4, Lane.L1) }, Frame60, 1.0);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldStart Miss", "#1 HoldRelease Perfect" }));
        }
    }
}
