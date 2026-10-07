using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class JudgeSampleKindTests
    {
        private static JudgeNote N(NoteKind kind) => new(1.0, Lane.L1, kind, kind == NoteKind.Tap ? -1 : 1);

        [Test]
        public void Hit_OnTapOrHead_IsPress()
        {
            Assert.That(JudgeSamples.Classify(JudgeEvent.Hit(0, N(NoteKind.Tap), JudgeType.Ideal, 0.05, 1.05)), Is.EqualTo(JudgeSampleKind.Press));
            Assert.That(JudgeSamples.Classify(JudgeEvent.Hit(0, N(NoteKind.HoldHead), JudgeType.Perfect, 0.0, 1.0)), Is.EqualTo(JudgeSampleKind.Press));
        }

        [Test]
        public void Hit_OnTail_IsRelease()
        {
            Assert.That(JudgeSamples.Classify(JudgeEvent.Hit(1, N(NoteKind.HoldTail), JudgeType.Kind, -0.1, 0.9)), Is.EqualTo(JudgeSampleKind.Release));
        }

        [Test]
        public void Miss_AtWindowClose_IsMiss()
        {
            Assert.That(JudgeSamples.Classify(JudgeEvent.Miss(0, N(NoteKind.Tap), 0.126, 1.126)), Is.EqualTo(JudgeSampleKind.Miss));
            Assert.That(JudgeSamples.Classify(JudgeEvent.Miss(1, N(NoteKind.HoldTail), 0.0, 1.0)), Is.EqualTo(JudgeSampleKind.Miss));
        }

        [Test]
        public void Miss_BeforeTheNote_IsSkipped()
        {
            Assert.That(JudgeSamples.Classify(JudgeEvent.Miss(1, N(NoteKind.HoldTail), -0.6, 0.4)), Is.EqualTo(JudgeSampleKind.Skip));
        }
    }
}
