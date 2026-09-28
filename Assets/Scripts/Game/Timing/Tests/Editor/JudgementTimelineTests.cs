using NUnit.Framework;
using SCOdyssey.Game.Timing.Judgement;

namespace SCOdyssey.Game.Timing.Tests
{
    public class JudgementTimelineTests
    {
        [Test]
        public void ToJudgeTime_SubtractsStepsTimesThreeMs()
        {
            var timeline = new JudgementTimeline(() => 2);
            timeline.Latch();

            Assert.AreEqual(9.994, timeline.ToJudgeTime(10.0), 1e-12);
        }

        [Test]
        public void BeforeLatch_NoOffsetIsApplied()
        {
            var timeline = new JudgementTimeline(() => 5);

            Assert.IsFalse(timeline.IsLatched);
            Assert.AreEqual(1.0, timeline.ToJudgeTime(1.0), 1e-12);
        }

        [Test]
        public void Latch_ReadsSettingOnlyOnce()
        {
            int reads = 0;
            int steps = 4;
            var timeline = new JudgementTimeline(() =>
            {
                reads++;
                return steps;
            });

            timeline.Latch();
            steps = -7;
            timeline.Latch();

            Assert.AreEqual(1, reads);
            Assert.AreEqual(4, timeline.Steps);
        }

        [Test]
        public void Unlatch_ReadsAgainForNextSession()
        {
            int steps = 1;
            var timeline = new JudgementTimeline(() => steps);
            timeline.Latch();

            steps = 3;
            timeline.Unlatch();
            timeline.Latch();

            Assert.AreEqual(3, timeline.Steps);
        }

        [Test]
        public void NullReader_LatchesZero()
        {
            var timeline = new JudgementTimeline(null);
            timeline.Latch();

            Assert.AreEqual(0, timeline.Steps);
        }
    }
}
