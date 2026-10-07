using System.Collections.Generic;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class RhythmSessionTests
    {
        private static JudgeNote N(double time, Lane lane, NoteKind kind = NoteKind.Tap, int pairId = -1) => new(time, lane, kind, pairId);

        // 버스로 나간 순서를 한 줄씩 기록한다
        private sealed class BusLog
        {
            public readonly List<string> Lines = new();
            public readonly List<JudgeEvent> Judged = new();

            public BusLog(IJudgementBus bus)
            {
                bus.LaneInput += e => Lines.Add($"input {e.Lane} {(e.IsPressed ? "P" : "R")}");
                bus.NoteJudged += e =>
                {
                    Judged.Add(e);
                    Lines.Add($"judged #{e.NoteId} {(e.IsMiss ? "Miss" : e.Judge.ToString())}");
                };
            }
        }

        private static RhythmSession Create(JudgeNote[] notes, out BusLog log, out JudgeEngine engine)
        {
            var bus = new JudgementBus();
            log = new BusLog(bus);
            engine = new JudgeEngine(JudgeSettings.Default);
            engine.Load(notes);
            return new RhythmSession(engine, bus);
        }

        [Test]
        public void Press_PublishesLaneInputBeforeJudgement()
        {
            RhythmSession session = Create(new[] { N(1.0, Lane.L2) }, out BusLog log, out _);

            session.Press(Lane.L2, 1.0);

            Assert.That(log.Lines, Is.EqualTo(new[] { "input L2 P", "judged #0 Perfect" }));
        }

        [Test]
        public void PressAndRelease_WithNothingToJudge_PublishOnlyLaneInput()
        {
            RhythmSession session = Create(new[] { N(5.0, Lane.L1) }, out BusLog log, out _);

            session.Press(Lane.L3, 1.0);
            session.Release(Lane.L3, 1.1);

            Assert.That(log.Lines, Is.EqualTo(new[] { "input L3 P", "input L3 R" }));
        }

        [Test]
        public void Release_JudgesHoldTail()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            session.Advance(1.5);
            session.Release(Lane.L1, 2.0);

            Assert.That(log.Lines, Is.EqualTo(new[] { "input L1 P", "judged #0 Perfect", "input L1 R", "judged #1 Perfect" }));
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void Release_BeforeTailWindow_MissesTailAtReleaseTime()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            session.Release(Lane.L1, 1.4);

            Assert.That(log.Judged, Has.Count.EqualTo(2));
            Assert.That(log.Judged[1].NoteId, Is.EqualTo(1));
            Assert.That(log.Judged[1].IsMiss, Is.True);
            Assert.That(log.Judged[1].Time, Is.EqualTo(1.4));
        }

        [Test]
        public void Advance_PublishesMissesInTimeOrder()
        {
            RhythmSession session = Create(new[] { N(1.0, Lane.L3), N(1.05, Lane.L1), N(1.1, Lane.L2) }, out BusLog log, out _);

            session.Advance(3.0);

            Assert.That(log.Judged, Has.Count.EqualTo(3));
            Assert.That(log.Judged, Has.All.Matches<JudgeEvent>(e => e.IsMiss));
            Assert.That(log.Judged[0].Time, Is.LessThanOrEqualTo(log.Judged[1].Time));
            Assert.That(log.Judged[1].Time, Is.LessThanOrEqualTo(log.Judged[2].Time));
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void Press_OnTap_ReturnsTheHit()
        {
            RhythmSession session = Create(new[] { N(1.0, Lane.L2) }, out _, out _);

            PressOutcome outcome = session.Press(Lane.L2, 1.03);

            Assert.That(outcome.Hit, Is.True);
            Assert.That(outcome.NoteId, Is.EqualTo(0));
            Assert.That(outcome.Kind, Is.EqualTo(NoteKind.Tap));
            Assert.That(outcome.Judge, Is.EqualTo(JudgeType.Master));
        }

        [Test]
        public void Press_OnHoldHead_ReturnsTheHeadHit()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out _, out _);

            PressOutcome outcome = session.Press(Lane.L1, 1.0);

            Assert.That(outcome.Hit, Is.True);
            Assert.That(outcome.Kind, Is.EqualTo(NoteKind.HoldHead));
            Assert.That(outcome.Judge, Is.EqualTo(JudgeType.Perfect));
        }

        [Test]
        public void Press_OnNextHeadWhileStillHolding_ReturnsTheNewHead_NotTheBrokenTail()
        {
            RhythmSession session = Create(
                new[]
                {
                    N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0),
                    N(2.1, Lane.L1, NoteKind.HoldHead, 3), N(3.0, Lane.L1, NoteKind.HoldTail, 2),
                },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            PressOutcome outcome = session.Press(Lane.L1, 2.1);

            Assert.That(outcome.Hit, Is.True);
            Assert.That(outcome.NoteId, Is.EqualTo(2));
            Assert.That(outcome.Kind, Is.EqualTo(NoteKind.HoldHead));
            Assert.That(log.Lines, Is.EqualTo(new[] { "input L1 P", "judged #0 Perfect", "input L1 P", "judged #2 Perfect", "judged #1 Miss" }));
        }

        [Test]
        public void Press_Whiff_ReturnsNoTarget()
        {
            RhythmSession session = Create(new[] { N(5.0, Lane.L1) }, out BusLog log, out _);

            PressOutcome otherLane = session.Press(Lane.L3, 1.0);
            PressOutcome outsideWindow = session.Press(Lane.L1, 1.0);

            Assert.That(otherLane.Hit, Is.False);
            Assert.That(outsideWindow.Hit, Is.False);
            Assert.That(log.Judged, Is.Empty);
        }

        [Test]
        public void Press_ThatOnlyFlushesEarlierMisses_ReturnsNoTarget()
        {
            RhythmSession session = Create(new[] { N(1.0, Lane.L1) }, out BusLog log, out _);

            PressOutcome outcome = session.Press(Lane.L1, 3.0);

            Assert.That(outcome.Hit, Is.False);
            Assert.That(log.Lines, Is.EqualTo(new[] { "input L1 P", "judged #0 Miss" }));
        }

        [Test]
        public void ReleaseUnjudged_PublishesOnlyTheInput_AndKeepsTheTailInProgress()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            session.ReleaseUnjudged(Lane.L1, 1.4);

            Assert.That(log.Lines, Is.EqualTo(new[] { "input L1 P", "judged #0 Perfect", "input L1 R" }));
            Assert.That(session.Reader.StatusOf(1), Is.EqualTo(NoteStatus.InProgress));
        }

        [Test]
        public void ReleaseUnjudged_WithoutRegrip_TailMissesWhenItsWindowCloses()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            session.ReleaseUnjudged(Lane.L1, 1.4);
            session.Advance(3.0);

            Assert.That(log.Judged, Has.Count.EqualTo(2));
            Assert.That(log.Judged[1].NoteId, Is.EqualTo(1));
            Assert.That(log.Judged[1].IsMiss, Is.True);
            Assert.That(log.Judged[1].Time, Is.EqualTo(2.0 + JudgeSettings.Default.TailWindow).Within(1e-9));
        }

        [Test]
        public void ReleaseUnjudged_ThenRegripAndRelease_GradesTheTail()
        {
            RhythmSession session = Create(
                new[] { N(1.0, Lane.L1, NoteKind.HoldHead, 1), N(2.0, Lane.L1, NoteKind.HoldTail, 0) },
                out BusLog log, out _);

            session.Press(Lane.L1, 1.0);
            session.ReleaseUnjudged(Lane.L1, 1.4);
            PressOutcome regrip = session.Press(Lane.L1, 1.6);
            session.Release(Lane.L1, 2.0);

            Assert.That(regrip.Hit, Is.False);
            Assert.That(log.Judged, Has.Count.EqualTo(2));
            Assert.That(log.Judged[1].NoteId, Is.EqualTo(1));
            Assert.That(log.Judged[1].IsMiss, Is.False);
            Assert.That(log.Judged[1].Judge, Is.EqualTo(JudgeType.Perfect));
            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void Reader_IsTheEngine()
        {
            RhythmSession session = Create(new[] { N(1.0, Lane.L4) }, out _, out JudgeEngine engine);

            session.Press(Lane.L4, 1.03);

            Assert.That(session.Reader, Is.SameAs(engine));
            Assert.That(session.Reader.StatusOf(0), Is.EqualTo(NoteStatus.Judged));
            Assert.That(session.IsFinished, Is.EqualTo(engine.IsFinished));
        }
    }
}
