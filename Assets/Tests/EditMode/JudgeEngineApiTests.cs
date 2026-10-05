using System;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    // 설정으로 바꿀 수 있는 부분과 상태 조회 API
    public class JudgeEngineApiTests
    {
        private static JudgeNote N(double time, Lane lane, NoteKind kind = NoteKind.Tap, int pair = -1) => new(time, lane, kind, pair);

        [Test]
        public void Windows_Default_IsShippedMilliseconds()
        {
            JudgeWindows w = JudgeWindows.Default;

            Assert.That(new[] { w.Perfect, w.Master, w.Ideal, w.Kind, w.Umm },
                Is.EqualTo(new[] { 0.021, 0.042, 0.084, 0.105, 0.126 }));
        }

        [TestCase(0, 42, 84, 105, 126)]
        [TestCase(21, 20, 84, 105, 126)]
        [TestCase(21, 42, 84, 130, 126)]
        public void Windows_MustBePositiveAndAscending(double p, double m, double i, double k, double u)
        {
            Assert.Throws<ArgumentException>(() => JudgeWindows.FromMilliseconds(p, m, i, k, u));
        }

        [Test]
        public void Windows_EqualValuesAreAllowed()
        {
            Assert.DoesNotThrow(() => JudgeWindows.FromMilliseconds(30, 30, 30, 30, 30));
        }

        [Test]
        public void CustomWindows_NarrowUmm_RejectsAndSweepsEarlier()
        {
            var settings = new JudgeSettings(JudgeWindows.FromMilliseconds(10, 15, 20, 25, 30));

            var pressed = new JudgeHarness(new[] { N(1.0, Lane.L1) }, settings);
            Assert.That(pressed.Press(Lane.L1, 1.05), Is.False, "기본 윈도우라면 Ideal");

            var graded = new JudgeHarness(new[] { N(1.0, Lane.L1) }, settings);
            graded.Press(Lane.L1, 1.022);
            Assert.That(graded.Outcomes(), Is.EqualTo(new[] { "#0 Tap Kind" }));

            var swept = new JudgeHarness(new[] { N(1.0, Lane.L1) }, settings);
            swept.Advance(1.035);
            Assert.That(swept.Outcomes(), Is.EqualTo(new[] { "#0 Tap Miss" }));
        }

        [Test]
        public void Nearest_PicksClosestNoteInWindow()
        {
            var settings = new JudgeSettings(JudgeWindows.Default, NoteSelectPolicy.Nearest);
            var h = new JudgeHarness(new[] { N(0.0, Lane.L1), N(0.1, Lane.L1) }, settings);

            h.Press(Lane.L1, 0.09);

            Assert.That(h.Events.ConvertAll(JudgeHarness.Describe), Is.EqualTo(new[] { "#1 Tap Perfect" }));
        }

        [Test]
        public void Nearest_TieGoesToEarlierNote()
        {
            var settings = new JudgeSettings(JudgeWindows.Default, NoteSelectPolicy.Nearest);
            var h = new JudgeHarness(new[] { N(0.0, Lane.L1), N(0.25, Lane.L1) }, settings);

            h.Press(Lane.L1, 0.125);

            Assert.That(h.Single(0).Judge, Is.EqualTo(JudgeType.Umm));
        }

        [Test]
        public void Reader_ReportsStatusGradeAndHeldKeys()
        {
            var h = new JudgeHarness(new[] { N(0.0, Lane.L1), N(0.0, Lane.L2), N(1.0, Lane.L3) });
            IJudgeStateReader reader = h.Engine;

            Assert.That(reader.Count, Is.EqualTo(3));
            Assert.That(reader.NoteAt(2).Lane, Is.EqualTo(Lane.L3));
            Assert.That(reader.StatusOf(0), Is.EqualTo(NoteStatus.Pending));
            Assert.That(reader.GradeOf(0), Is.Null);

            h.Press(Lane.L1, 0.03);
            Assert.That(reader.IsHeld(Lane.L1), Is.True);
            Assert.That(reader.IsHeld(Lane.L2), Is.False);
            Assert.That(reader.StatusOf(0), Is.EqualTo(NoteStatus.Judged));
            Assert.That(reader.GradeOf(0), Is.EqualTo(JudgeType.Master));

            h.Release(Lane.L1, 0.1);
            h.Advance(0.5);
            Assert.That(reader.IsHeld(Lane.L1), Is.False);
            Assert.That(reader.StatusOf(1), Is.EqualTo(NoteStatus.Missed));
            Assert.That(reader.GradeOf(1), Is.Null, "miss에는 등급이 없다");
            Assert.That(reader.StatusOf(2), Is.EqualTo(NoteStatus.Pending));
            Assert.That(reader.IsFinished, Is.False);
        }

        [Test]
        public void Advance_NowNeverMovesBackward()
        {
            var h = new JudgeHarness(new[] { N(0.5, Lane.L1) });
            Assert.That(h.Engine.Now, Is.EqualTo(double.NegativeInfinity));

            h.Advance(1.0);
            h.Advance(0.2);

            Assert.That(h.Engine.Now, Is.EqualTo(1.0));
            Assert.That(h.Outcomes(), Is.EqualTo(new[] { "#0 Tap Miss" }));
        }

        [Test]
        public void Events_CarrySignedDeltaAndDecisionTime()
        {
            const double offset = 0.01;
            var h = new JudgeHarness(new[]
            {
                N(1.0, Lane.L1),
                N(2.0, Lane.L1),
                N(3.0, Lane.L1),
                N(4.0, Lane.L2, NoteKind.HoldHead, 4),
                N(4.5, Lane.L2, NoteKind.HoldTail, 3),
            }, offset);
            double umm = JudgeWindows.Default.Umm;

            h.Press(Lane.L1, 1.04);   // 판정 시각 1.01, 30ms 늦음
            h.Press(Lane.L1, 1.99);   // 판정 시각 2.01, 20ms 이름
            h.Advance(3.5);           // 3.01은 3.01 + Umm에 miss
            h.Press(Lane.L2, 4.01);
            h.Release(Lane.L2, 4.52); // 꼬리 판정 시각 4.51, 10ms 늦게 뗌

            JudgeEvent late = h.Single(0), early = h.Single(1), miss = h.Single(2), tail = h.Single(4);
            Assert.That(late.DeltaSec, Is.EqualTo(0.03).Within(1e-12));
            Assert.That(late.Time, Is.EqualTo(1.04));
            Assert.That(early.DeltaSec, Is.EqualTo(-0.02).Within(1e-12));
            Assert.That(early.Time, Is.EqualTo(1.99));
            Assert.That(miss.IsMiss, Is.True);
            Assert.That(miss.DeltaSec, Is.EqualTo(umm));
            Assert.That(miss.Time, Is.EqualTo(3.0 + offset + umm).Within(1e-12));
            Assert.That(tail.DeltaSec, Is.EqualTo(0.01).Within(1e-12));
            Assert.That(tail.Time, Is.EqualTo(4.52));
            Assert.That(tail.PairId, Is.EqualTo(3));
        }

        [Test]
        public void Reset_RestoresInitialState()
        {
            var h = new JudgeHarness(new[] { N(0.0, Lane.L1), N(1.0, Lane.L2) });
            h.Press(Lane.L1, 0.0);
            h.Advance(2.0);
            Assert.That(h.Engine.IsFinished, Is.True);

            h.Engine.Reset();

            Assert.That(h.Engine.IsFinished, Is.False);
            Assert.That(h.Engine.Now, Is.EqualTo(double.NegativeInfinity));
            Assert.That(h.Engine.IsHeld(Lane.L1), Is.False);
            Assert.That(h.Engine.StatusOf(0), Is.EqualTo(NoteStatus.Pending));
            Assert.That(h.Engine.StatusOf(1), Is.EqualTo(NoteStatus.Pending));
        }

        [Test]
        public void Advance_EmitsEventsInTimeOrder()
        {
            // 꼬리 시간 초과(1.226)는 레인 루프에서, 탭 스윕(1.176)은 커서 루프에서 나온다. 한 번에 진행해도 시각순이어야 한다
            var engine = new JudgeEngine(JudgeSettings.Default);
            engine.Load(new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 2), N(1.05, Lane.L2), N(1.1, Lane.L1, NoteKind.HoldTail, 0) });
            var events = new System.Collections.Generic.List<JudgeEvent>();
            engine.Press(Lane.L1, 1.0, events);
            events.Clear();

            engine.Advance(2.0, events);

            Assert.That(events.ConvertAll(e => e.NoteId), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(events[0].Time, Is.LessThan(events[1].Time));
        }

        [Test]
        public void Advance_SameTimeMisses_KeepHeadBeforeTail()
        {
            var engine = new JudgeEngine(JudgeSettings.Default);
            engine.Load(new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) });
            var events = new System.Collections.Generic.List<JudgeEvent>();

            engine.Advance(3.0, events);

            Assert.That(events.ConvertAll(e => e.NoteId), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(events[0].Time, Is.EqualTo(events[1].Time), "머리가 놓친 순간 꼬리도 같이 죽는다");
        }

        private static readonly object[] InvalidTracks =
        {
            new object[] { "시간 역순", new[] { N(2.0, Lane.L1), N(1.0, Lane.L2) } },
            new object[] { "짝이 서로 안 가리킴", new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail) } },
            new object[] { "다른 레인 짝", new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L2, NoteKind.HoldTail, 0) } },
            new object[] { "꼬리가 머리보다 앞", new[] { N(1.0, Lane.L1, NoteKind.HoldTail, 1), N(2.0, Lane.L1, NoteKind.HoldHead, 0) } },
            new object[] { "탭에 짝", new[] { N(1.0, Lane.L1, NoteKind.Tap, 1), N(2.0, Lane.L1, NoteKind.Tap, 0) } },
            new object[] { "짝 인덱스 범위 밖", new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 5) } },
            new object[] { "자기 자신이 짝", new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 0) } },
        };

        [TestCaseSource(nameof(InvalidTracks))]
        public void Load_RejectsBrokenTrack(string reason, JudgeNote[] track)
        {
            var engine = new JudgeEngine(JudgeSettings.Default);

            Assert.Throws<ArgumentException>(() => engine.Load(track), reason);
        }

        [Test]
        public void Load_AcceptsSameTimeNotesAndUnpairedNotes()
        {
            var engine = new JudgeEngine(JudgeSettings.Default);
            JudgeNote[] track =
            {
                N(1.0, Lane.L1), N(1.0, Lane.L2, NoteKind.HoldHead, 3), N(1.5, Lane.L3, NoteKind.HoldTail), N(2.0, Lane.L2, NoteKind.HoldTail, 1),
            };

            Assert.DoesNotThrow(() => engine.Load(track));
            Assert.DoesNotThrow(() => engine.Load(null));
            Assert.That(engine.Count, Is.Zero);
        }
    }
}
