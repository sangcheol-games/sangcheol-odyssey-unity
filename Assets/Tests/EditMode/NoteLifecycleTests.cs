using System.Collections.Generic;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    public class NoteLifecycleTests
    {
        private static JudgeNote N(double time, Lane lane, NoteKind kind = NoteKind.Tap, int pairId = -1) => new(time, lane, kind, pairId);

        private readonly List<JudgeEvent> _events = new();

        private static NoteLifecycle Create(JudgeNote[] notes, out JudgeEngine engine)
        {
            engine = new JudgeEngine(JudgeSettings.Default);
            engine.Load(notes);
            return new NoteLifecycle(new BarClock(2.0), engine);
        }

        [Test]
        public void Phase_SpawnsOneBarAhead_ActiveAtBarStart()
        {
            NoteLifecycle life = Create(new[] { N(6.5, Lane.L1) }, out _);

            Assert.That(life.SpawnAt(3), Is.EqualTo(4.0));
            Assert.That(life.ActiveAt(3), Is.EqualTo(6.0));
            Assert.That(life.PhaseAt(3, 3.99), Is.EqualTo(NotePhase.NotSpawned));
            Assert.That(life.PhaseAt(3, 4.0), Is.EqualTo(NotePhase.Ghost));
            Assert.That(life.PhaseAt(3, 5.99), Is.EqualTo(NotePhase.Ghost));
            Assert.That(life.PhaseAt(3, 6.0), Is.EqualTo(NotePhase.Active));
        }

        [Test]
        public void IsDecided_FollowsEngine_HitAndMiss()
        {
            NoteLifecycle life = Create(new[] { N(1.0, Lane.L1), N(1.5, Lane.L2) }, out JudgeEngine engine);

            Assert.That(life.IsDecided(0), Is.False);
            Assert.That(life.IsDecided(-1), Is.False, "스폰 안 하는 본체(id -1)");

            engine.Press(Lane.L1, 1.0, _events);
            Assert.That(life.IsDecided(0), Is.True);
            Assert.That(life.IsDecided(1), Is.False);

            engine.Advance(2.0, _events);
            Assert.That(life.IsDecided(1), Is.True);
        }

        [Test]
        public void ShouldActivate_SkipsTapJudgedWhileGhost()
        {
            // 마디 시작(2.0) 직전에 일찍 친 탭: Ghost일 때 판정됐으니 Active로 올리지 않는다
            NoteLifecycle life = Create(new[] { N(2.0, Lane.L1) }, out JudgeEngine engine);

            engine.Press(Lane.L1, 1.95, _events);

            Assert.That(life.PhaseAt(1, 1.95), Is.EqualTo(NotePhase.Ghost));
            Assert.That(life.IsDecided(0), Is.True);
            Assert.That(life.ShouldActivate(0), Is.False);
        }

        [Test]
        public void ShouldActivate_KeepsJudgedHoldHead_ForItsHoldBar()
        {
            NoteLifecycle life = Create(new[] { N(2.0, Lane.L1, NoteKind.HoldHead, 1), N(3.0, Lane.L1, NoteKind.HoldTail, 0) }, out JudgeEngine engine);

            Assert.That(life.ShouldActivate(0), Is.True);
            engine.Press(Lane.L1, 1.95, _events);

            Assert.That(life.IsDecided(0), Is.True);
            Assert.That(life.ShouldActivate(0), Is.True);
            Assert.That(life.IsDecided(1), Is.False, "누르는 중인 꼬리는 아직");
        }

        [Test]
        public void MissedHead_DecidesItsTailToo()
        {
            NoteLifecycle life = Create(new[] { N(2.0, Lane.L1, NoteKind.HoldHead, 1), N(3.0, Lane.L1, NoteKind.HoldTail, 0) }, out JudgeEngine engine);

            engine.Advance(2.2, _events);

            Assert.That(life.IsDecided(0), Is.True);
            Assert.That(life.IsDecided(1), Is.True);
            Assert.That(life.ShouldActivate(1), Is.False);
        }
    }
}
