using System.Collections.Generic;
using NUnit.Framework;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class JudgementBusTests
    {
        [Test]
        public void Publish_ReachesSubscribers_UntilUnsubscribed()
        {
            var bus = new JudgementBus();
            var inputs = new List<LaneInputEvent>();
            var judged = new List<JudgeEvent>();
            void OnInput(LaneInputEvent e) => inputs.Add(e);
            void OnJudged(JudgeEvent e) => judged.Add(e);
            bus.LaneInput += OnInput;
            bus.NoteJudged += OnJudged;

            var note = new JudgeNote(1.0, Lane.L3, NoteKind.Tap);
            bus.PublishLaneInput(new LaneInputEvent(Lane.L3, true, 1.01));
            bus.PublishNoteJudged(JudgeEvent.Hit(0, note, JudgeType.Perfect, 0.01, 1.01));
            bus.LaneInput -= OnInput;
            bus.NoteJudged -= OnJudged;
            bus.PublishLaneInput(new LaneInputEvent(Lane.L3, false, 1.2));
            bus.PublishNoteJudged(JudgeEvent.Miss(0, note, 0.126, 1.126));

            Assert.That(inputs, Has.Count.EqualTo(1));
            Assert.That(inputs[0].Lane, Is.EqualTo(Lane.L3));
            Assert.That(inputs[0].IsPressed, Is.True);
            Assert.That(inputs[0].Time, Is.EqualTo(1.01));
            Assert.That(judged, Has.Count.EqualTo(1));
            Assert.That(judged[0].Judge, Is.EqualTo(JudgeType.Perfect));
        }

        [Test]
        public void Publish_WithoutSubscribers_DoesNothing()
        {
            var bus = new JudgementBus();

            Assert.DoesNotThrow(() => bus.PublishLaneInput(new LaneInputEvent(Lane.L1, true, 0)));
        }
    }
}
