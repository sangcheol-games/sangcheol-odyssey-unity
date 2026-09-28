using NUnit.Framework;
using SCOdyssey.Audio.Clock;

namespace SCOdyssey.Audio.Tests
{
    public class SongAnchorTests
    {
        [Test]
        public void AudioZero_PositiveOffsetStartsMusicLater()
        {
            Assert.AreEqual(1.02, SongAnchor.AudioZero(1.0, 20), 1e-12);
            Assert.AreEqual(0.98, SongAnchor.AudioZero(1.0, -20), 1e-12);
        }

        [Test]
        public void AudioZero_UsesRealDivision()
        {
            Assert.AreEqual(0.001, SongAnchor.AudioZero(0, 1), 1e-12);
        }

        [Test]
        public void CommitLead_IsThreeBlocksOrFifteenMs()
        {
            Assert.AreEqual(768UL, SongAnchor.CommitLead(256, 48000));
            Assert.AreEqual(720UL, SongAnchor.CommitLead(64, 48000));
            Assert.AreEqual(662UL, SongAnchor.CommitLead(128, 44100));    // ceil(661.5)
        }

        [Test]
        public void LeadInRemaining_DelaysSoundAndSkipsSeek()
        {
            double position = SongAnchor.AudioPosition(0.75, 1.0);

            Assert.AreEqual(0L, SongAnchor.SeekFrames(position, 44100));
            Assert.AreEqual(1000UL + 12000UL, SongAnchor.SoundStartDsp(1000, position, 48000));
        }

        [Test]
        public void ResumeInsideAudio_SeeksAndStartsAtClockStart()
        {
            double position = SongAnchor.AudioPosition(3.5, 1.0);

            Assert.AreEqual(110250L, SongAnchor.SeekFrames(position, 44100));
            Assert.AreEqual(1000UL, SongAnchor.SoundStartDsp(1000, position, 48000));
        }
    }
}
