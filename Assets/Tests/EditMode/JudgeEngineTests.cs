using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    // 기본 설정의 JudgeEngine 판정 규칙. 규칙이 바뀌면 여기서 먼저 깨진다.
    public class JudgeEngineTests
    {
        private const double Eps = 1e-9;
        private const double Frame60 = 1.0 / 60;
        private static readonly double U = JudgeWindows.Default.Umm;

        private static JudgeNote N(double time, Lane lane, NoteKind kind = NoteKind.Tap, int pair = -1) => new(time, lane, kind, pair);

        // 레인1 홀드 하나: 머리 #0, 꼬리 #1
        private static JudgeNote[] Hold(double head, double tail)
            => new[] { N(head, Lane.L1, NoteKind.HoldHead, 1), N(tail, Lane.L1, NoteKind.HoldTail, 0) };

        private static JudgeHarness Harness(params JudgeNote[] notes) => new(notes);

        private static ScriptedInput Press(double time, Lane lane) => ScriptedInput.Press(time, lane);
        private static ScriptedInput Release(double time, Lane lane) => ScriptedInput.Release(time, lane);

        private static double WindowOf(JudgeType grade) => grade switch
        {
            JudgeType.Perfect => JudgeWindows.Default.Perfect,
            JudgeType.Master => JudgeWindows.Default.Master,
            JudgeType.Ideal => JudgeWindows.Default.Ideal,
            JudgeType.Kind => JudgeWindows.Default.Kind,
            _ => JudgeWindows.Default.Umm,
        };

        private static JudgeType? PressGrade(double noteTime, double pressTime)
        {
            JudgeHarness h = Harness(N(noteTime, Lane.L1));
            return h.Press(Lane.L1, pressTime) ? h.Events[0].Judge : null;
        }

        // ───────────── 탭 ─────────────

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
            Assert.That(PressGrade(0, U), Is.Null, "late by exactly Umm");
            Assert.That(PressGrade(U, 0), Is.Null, "early by exactly Umm");
            Assert.That(PressGrade(0, U - Eps), Is.EqualTo(JudgeType.Umm));
            Assert.That(PressGrade(U - Eps, 0), Is.EqualTo(JudgeType.Umm));
        }

        [Test]
        public void Press_OutsideUmm_NoClaim_ThenSweptAsMiss()
        {
            JudgeHarness h = Harness(N(1.0, Lane.L1));

            Assert.That(h.Press(Lane.L1, 0.8), Is.False);
            Assert.That(h.Events, Is.Empty);
            Assert.That(h.Press(Lane.L1, 1.2), Is.False, "입력 시각까지 먼저 진행하므로 이 누름 직전에 miss가 확정된다");
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 Tap Miss" }));
        }

        [Test]
        public void Press_OtherLane_NoClaim()
        {
            JudgeHarness h = Harness(N(0, Lane.L1));

            Assert.That(h.Press(Lane.L2, 0), Is.False);
            Assert.That(h.Press(Lane.L1, 0), Is.True);
        }

        [TestCase(NoteKind.Tap, true)]
        [TestCase(NoteKind.HoldHead, true)]
        [TestCase(NoteKind.HoldTail, false)]
        public void Press_ClaimsOnlyTapAndHead(NoteKind kind, bool claimed)
        {
            Assert.That(Harness(N(0, Lane.L1, kind)).Press(Lane.L1, 0), Is.EqualTo(claimed));
        }

        [TestCase(NoteKind.Tap)]
        [TestCase(NoteKind.HoldHead)]
        [TestCase(NoteKind.HoldTail)]
        public void Release_WithoutHoldInProgress_DecidesNothing(NoteKind kind)
        {
            JudgeHarness h = Harness(N(0, Lane.L1, kind));

            Assert.That(h.Release(Lane.L1, 0), Is.False);
            Assert.That(h.Events, Is.Empty);
        }

        [Test]
        public void Earliest_WinsOverlappingWindows()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1), N(0.1, Lane.L1));

            // 두 번째 노트가 더 가깝지만 앞 노트를 집는다
            Assert.That(h.Press(Lane.L1, 0.09), Is.True);
            Assert.That(h.Press(Lane.L1, 0.1), Is.True);

            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#0 Tap Kind", "#1 Tap Perfect" }));
        }

        [Test]
        public void MissSweep_OrderAndTiming()
        {
            JudgeHarness h = Harness(N(0.00, Lane.L1), N(0.05, Lane.L2), N(0.10, Lane.L3), N(0.30, Lane.L4));

            h.Advance(U);   // T + Umm 딱 그 시각은 아직 miss가 아니다
            Assert.That(h.Events, Is.Empty);

            h.Advance(0.2);
            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#0 Tap Miss", "#1 Tap Miss" }));
            Assert.That(h.Events.All(e => e.IsMiss && e.Judge == JudgeType.Umm), Is.True);
            Assert.That(h.Engine.IsFinished, Is.False);

            h.Advance(1.0);
            Assert.That(h.Events.Select(e => e.NoteId), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(h.Engine.IsFinished, Is.True);
        }

        [Test]
        public void MissSweep_SkipsJudgedNotes()
        {
            JudgeHarness h = Harness(N(0.00, Lane.L1), N(0.05, Lane.L2));

            h.Press(Lane.L2, 0.05);
            h.Advance(1.0);

            Assert.That(h.Events.Select(JudgeHarness.Describe), Is.EqualTo(new[] { "#1 Tap Perfect", "#0 Tap Miss" }));
            Assert.That(h.Engine.IsFinished, Is.True);
        }

        [Test]
        public void SweptNote_CannotBeClaimed_EvenByPressTimestampedInsideWindow()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1));

            h.Advance(0.2);

            Assert.That(h.Press(Lane.L1, 0.05), Is.False);
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 Tap Miss" }));
        }

        [Test]
        public void IsFinished_AsSoonAsEveryNoteIsDecided()
        {
            JudgeHarness h = Harness(N(1.0, Lane.L1));

            h.Press(Lane.L1, 0.95);

            Assert.That(h.Engine.IsFinished, Is.True, "윈도우가 닫히기를 기다리지 않는다");
        }

        [TestCase(+0.05)]
        [TestCase(-0.05)]
        public void JudgementOffset_ShiftsWindowCenter(double offset)
        {
            var notes = new[] { N(1.0, Lane.L1) };

            var shifted = new JudgeHarness(notes, offset);
            shifted.Press(Lane.L1, 1.0 + offset);
            Assert.That(shifted.Outcomes(), Is.EqualTo(new[] { "#0 Tap Perfect" }));

            var unshifted = new JudgeHarness(notes, offset);
            unshifted.Press(Lane.L1, 1.0);
            Assert.That(unshifted.Outcomes(), Is.EqualTo(new[] { "#0 Tap Ideal" }));

            var sweep = new JudgeHarness(notes, offset);
            sweep.Advance(1.0 + offset + U - 0.01);
            Assert.That(sweep.Events, Is.Empty);
            sweep.Advance(1.0 + offset + U + 0.01);
            Assert.That(sweep.Outcomes(), Is.EqualTo(new[] { "#0 Tap Miss" }));
        }

        // ───────────── 홀드: 머리(누르기) + 꼬리(떼기) ─────────────

        [Test]
        public void Hold_HeadAndTailGraded()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));

            h.Press(Lane.L1, 0.0);
            Assert.That(h.Engine.StatusOf(1), Is.EqualTo(NoteStatus.InProgress));
            Assert.That(h.Engine.HoldInProgressOf(Lane.L1), Is.EqualTo(1));

            Assert.That(h.Release(Lane.L1, 0.43), Is.True);
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect", "#1 HoldTail Master" }));
            Assert.That(h.Engine.HoldInProgressOf(Lane.L1), Is.EqualTo(-1));
            Assert.That(h.Engine.IsFinished, Is.True);
        }

        [Test]
        public void Hold_EarlyRelease_TailMissAtReleaseTime()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));
            h.Press(Lane.L1, 0.0);

            Assert.That(h.Release(Lane.L1, 0.2), Is.True);

            JudgeEvent tail = h.Single(1);
            Assert.That(tail.IsMiss, Is.True);
            Assert.That(tail.Time, Is.EqualTo(0.2));
            Assert.That(tail.DeltaSec, Is.EqualTo(-0.2).Within(1e-12));
            Assert.That(h.Engine.StatusOf(1), Is.EqualTo(NoteStatus.Missed));
        }

        [Test]
        public void Hold_ReleaseEarlyButInsideTailWindow_IsGraded()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));
            h.Press(Lane.L1, 0.0);

            h.Release(Lane.L1, 0.3);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect", "#1 HoldTail Kind" }));
        }

        [Test]
        public void Hold_HeadMissed_KillsTailAtTheSameTime()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));

            h.Advance(0.2);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Miss", "#1 HoldTail Miss" }));
            Assert.That(h.Single(1).Time, Is.EqualTo(U).Within(1e-12));
            Assert.That(h.Engine.IsFinished, Is.True);
        }

        [Test]
        public void Hold_HeldTooLong_TailMissAtWindowClose()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));
            h.Press(Lane.L1, 0.0);

            h.Advance(0.4 + U);   // 닫히는 순간은 아직
            Assert.That(h.Engine.StatusOf(1), Is.EqualTo(NoteStatus.InProgress));

            h.Advance(1.0);
            JudgeEvent tail = h.Single(1);
            Assert.That(tail.IsMiss, Is.True);
            Assert.That(tail.Time, Is.EqualTo(0.4 + U).Within(1e-12));
        }

        [Test]
        public void Hold_NoRecoveryAfterBreak()
        {
            JudgeHarness h = Harness(Hold(0.0, 0.4));
            h.Press(Lane.L1, 0.0);
            h.Release(Lane.L1, 0.1);

            Assert.That(h.Press(Lane.L1, 0.2), Is.False);
            Assert.That(h.Release(Lane.L1, 0.4), Is.False);
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect", "#1 HoldTail Miss" }));
        }

        [Test]
        public void Hold_ConsecutiveHoldsNeedNewPress()
        {
            JudgeHarness h = Harness(
                N(0.0, Lane.L1, NoteKind.HoldHead, 1), N(0.4, Lane.L1, NoteKind.HoldTail, 0),
                N(0.8, Lane.L1, NoteKind.HoldHead, 3), N(1.2, Lane.L1, NoteKind.HoldTail, 2));

            h.Run(new[] { Press(0.0, Lane.L1) }, Frame60, 1.5);

            Assert.That(h.Outcomes(), Is.EqualTo(new[]
            {
                "#0 HoldHead Perfect", "#1 HoldTail Miss",
                "#2 HoldHead Miss", "#3 HoldTail Miss",
            }));
        }

        [Test]
        public void Hold_PressNextHeadWithoutRelease_EndsPreviousHold()
        {
            JudgeHarness h = Harness(
                N(0.0, Lane.L1, NoteKind.HoldHead, 1), N(0.4, Lane.L1, NoteKind.HoldTail, 0),
                N(0.45, Lane.L1, NoteKind.HoldHead, 3), N(0.9, Lane.L1, NoteKind.HoldTail, 2));

            h.Press(Lane.L1, 0.0);
            h.Press(Lane.L1, 0.45);   // 떼지 않고 다음 머리

            Assert.That(h.Engine.StatusOf(1), Is.EqualTo(NoteStatus.Missed));
            Assert.That(h.Engine.HoldInProgressOf(Lane.L1), Is.EqualTo(3));
        }

        [Test]
        public void Hold_OrphanTail_IsMissedAtWindowClose()
        {
            JudgeHarness h = Harness(N(0.4, Lane.L1, NoteKind.HoldTail));

            Assert.That(h.Press(Lane.L1, 0.4), Is.False);
            Assert.That(h.Release(Lane.L1, 0.4), Is.False);
            h.Advance(1.0);

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldTail Miss" }));
        }

        [Test]
        public void Hold_UnpairedHead_IsJudgedLikeATap()
        {
            JudgeHarness h = Harness(N(0.0, Lane.L1, NoteKind.HoldHead));

            h.Press(Lane.L1, 0.01);

            Assert.That(h.Engine.HoldInProgressOf(Lane.L1), Is.EqualTo(-1));
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect" }));
            Assert.That(h.Engine.IsFinished, Is.True);
        }

        [Test]
        public void Hold_TailWindowScale_WidensReleaseWindowOnly()
        {
            var settings = new JudgeSettings(JudgeWindows.Default, tailWindowScale: 2.0);
            var h = new JudgeHarness(Hold(0.0, 0.4), settings);
            h.Press(Lane.L1, 0.0);

            h.Release(Lane.L1, 0.2);   // 200ms 일찍: 기본(126ms)이면 끊김, 2배(252ms)면 떼기 판정. 등급 경계는 그대로

            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect", "#1 HoldTail Umm" }));
        }

        [Test]
        public void Hold_FrameHitch_NoSpuriousMiss()
        {
            JudgeNote[] notes = Hold(0.0, 2.0);
            var inputs = new[] { Press(0.0, Lane.L1), Release(2.0, Lane.L1) };

            var smooth = new JudgeHarness(notes);
            smooth.Run(inputs, Frame60, 3.0);
            var hitched = new JudgeHarness(notes);
            var script = new InputScript();
            foreach (ScriptedInput input in inputs) script.Add(input);
            ScriptedRun.Run(hitched.Engine, script, FrameSchedule.WithHitch(Frame60, 3.0, 0.97, 0.3), hitched.Events);

            Assert.That(hitched.Outcomes(), Is.EqualTo(smooth.Outcomes()));
            Assert.That(smooth.Outcomes(), Is.EqualTo(new[] { "#0 HoldHead Perfect", "#1 HoldTail Perfect" }));
        }

        // ───────────── 프레임 간격 무관성 ─────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TapsAndHolds_OutcomeAndDecisionTimeIndependentOfTickRate(int seed)
        {
            (JudgeNote[] track, List<ScriptedInput> inputs) = RandomSession(seed);
            double end = track[^1].Time + 1.0;

            string[] Play(IEnumerable<double> frames)
            {
                var h = new JudgeHarness(track);
                var script = new InputScript();
                foreach (ScriptedInput input in inputs) script.Add(input);
                ScriptedRun.Run(h.Engine, script, frames, h.Events);
                Assert.That(h.Events, Has.Count.EqualTo(track.Length), "노트마다 정확히 한 번씩 확정");
                return h.Events.OrderBy(e => e.NoteId)
                    .Select(e => $"{JudgeOutcome.Describe(e)} @{e.Time.ToString("0.000000", CultureInfo.InvariantCulture)}")
                    .ToArray();
            }

            string[] fine = Play(FrameSchedule.Uniform(0.001, end));
            Assert.That(Play(FrameSchedule.Uniform(0.1, end)), Is.EqualTo(fine), "100ms 프레임");
            Assert.That(Play(FrameSchedule.WithHitch(Frame60, end, end / 2, 0.5)), Is.EqualTo(fine), "500ms 히치");
        }

        // 레인 4개에 탭·홀드를 섞고, 입력은 ±120ms 흔들림 + 홀드 30%는 중간에 뗀다
        private static (JudgeNote[] track, List<ScriptedInput> inputs) RandomSession(int seed)
        {
            var rng = new Random(seed);
            var raw = new List<(double time, Lane lane, NoteKind kind, int key)>();
            var inputs = new List<ScriptedInput>();
            var freeAt = new double[LANE_COUNT];
            double t = 0.5;
            int holdKey = 0;

            for (int i = 0; i < 150; i++)
            {
                t += 0.05 + rng.NextDouble() * 0.15;
                var lane = (Lane)rng.Next(LANE_COUNT);
                if (t < freeAt[(int)lane]) continue;
                double press = t + (rng.NextDouble() - 0.5) * 0.24;

                if (rng.NextDouble() < 0.4)
                {
                    double end = t + 0.2 + rng.NextDouble() * 0.6;
                    raw.Add((t, lane, NoteKind.HoldHead, holdKey));
                    raw.Add((end, lane, NoteKind.HoldTail, holdKey));
                    holdKey++;

                    double release = rng.NextDouble() < 0.3 ? t + (end - t) * 0.5 : end + (rng.NextDouble() - 0.5) * 0.24;
                    inputs.Add(Press(press, lane));
                    inputs.Add(Release(Math.Max(release, press + 0.01), lane));
                    freeAt[(int)lane] = end + 0.3;
                }
                else
                {
                    raw.Add((t, lane, NoteKind.Tap, -1));
                    if (rng.NextDouble() < 0.85)
                    {
                        inputs.Add(Press(press, lane));
                        inputs.Add(Release(press + 0.02, lane));
                    }
                    freeAt[(int)lane] = t + 0.3;
                }
            }

            var sorted = raw.OrderBy(r => r.time).ThenBy(r => (int)r.lane).ToList();
            var track = new JudgeNote[sorted.Count];
            var headOf = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                (double time, Lane lane, NoteKind kind, int key) = sorted[i];
                track[i] = N(time, lane, kind);
                if (kind == NoteKind.HoldHead)
                {
                    headOf[key] = i;
                }
                else if (kind == NoteKind.HoldTail)
                {
                    int head = headOf[key];
                    track[head] = track[head].WithPair(i);
                    track[i] = track[i].WithPair(head);
                }
            }
            return (track, inputs);
        }
    }
}
