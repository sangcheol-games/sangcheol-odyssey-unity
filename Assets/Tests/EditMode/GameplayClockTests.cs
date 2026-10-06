using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    public class GameplayClockTests
    {
        private double _dsp;
        private GameplayClock _clock;

        [SetUp]
        public void SetUp()
        {
            _dsp = 100.0;
            _clock = new GameplayClock(() => _dsp);
        }

        [Test]
        public void BeforeStart_NowIsZero()
        {
            _dsp = 250.0;

            Assert.That(_clock.Now, Is.EqualTo(0));
            Assert.That(_clock.IsRunning, Is.False);
        }

        [Test]
        public void AfterStart_NowIsDspMinusOrigin()
        {
            _clock.Start();
            _dsp = 103.5;

            Assert.That(_clock.OriginDsp, Is.EqualTo(100.0));
            Assert.That(_clock.Now, Is.EqualTo(3.5).Within(1e-9));
        }

        [Test]
        public void Pause_FreezesNow_ResumeContinuesWithoutPausedTime()
        {
            _clock.Start();
            _dsp = 102.0;
            _clock.Pause();
            _dsp = 110.0;

            Assert.That(_clock.IsPaused, Is.True);
            Assert.That(_clock.Now, Is.EqualTo(2.0).Within(1e-9));

            _clock.Resume();
            Assert.That(_clock.Now, Is.EqualTo(2.0).Within(1e-9));

            _dsp = 111.0;
            Assert.That(_clock.IsPaused, Is.False);
            Assert.That(_clock.Now, Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void PauseTwice_KeepsFirstPausePoint()
        {
            _clock.Start();
            _dsp = 101.0;
            _clock.Pause();
            _dsp = 105.0;
            _clock.Pause();
            _clock.Resume();

            Assert.That(_clock.Now, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Stop_FreezesNowAtStopTime()
        {
            _clock.Start();
            _dsp = 104.0;
            _clock.Stop();
            _dsp = 120.0;

            Assert.That(_clock.IsRunning, Is.False);
            Assert.That(_clock.Now, Is.EqualTo(4.0).Within(1e-9));
        }

        [Test]
        public void Stop_WhilePaused_FreezesAtPausePoint()
        {
            _clock.Start();
            _dsp = 102.0;
            _clock.Pause();
            _dsp = 108.0;
            _clock.Stop();

            Assert.That(_clock.Now, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(_clock.IsPaused, Is.False);
        }

        [Test]
        public void ToChartTime_FollowsOriginShiftedByPause()
        {
            _clock.Start();
            Assert.That(_clock.ToChartTime(101.25), Is.EqualTo(1.25).Within(1e-9));

            _dsp = 102.0;
            _clock.Pause();
            _dsp = 107.0;
            _clock.Resume();

            Assert.That(_clock.ToChartTime(108.0), Is.EqualTo(3.0).Within(1e-9));
        }
    }
}
