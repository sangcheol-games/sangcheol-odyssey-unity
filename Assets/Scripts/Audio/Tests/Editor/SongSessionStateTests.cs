using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Playback;

namespace SCOdyssey.Audio.Tests
{
    // 엔진을 부팅하지 않으면 세션은 무음 세션(가상 QPC 클록)으로 동작한다. FMOD 없이 상태 머신을 확인한다.
    public class SongSessionStateTests
    {
        private ClockSampler _sampler;
        private FmodSongSession _session;
        private List<SongSessionEvent> _events;

        [SetUp]
        public void SetUp()
        {
            var engine = new AudioEngine();
            var mixer = new FmodMixer();
            _sampler = new ClockSampler(engine, mixer);
            _sampler.Reset();
            _sampler.Sample();
            _session = new FmodSongSession(engine, mixer, _sampler, "unused.wav", 0);
            _events = new List<SongSessionEvent>();
            _session.Changed += _events.Add;
        }

        private void RunFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Thread.Sleep(2);
                _sampler.Sample();
                _session.Tick();
            }
        }

        [Test]
        public void SilentSession_StartsIntoLeadInWithAudioFinished()
        {
            _session.Start(1.0, 0);

            Assert.AreEqual(SongSessionState.LeadIn, _session.State);
            Assert.IsTrue(_session.IsAudioFinished);
            Assert.AreEqual(new[] { SongSessionEvent.Started }, _events.ToArray());
        }

        [Test]
        public void NoLeadIn_GoesStraightToPlayingWithAudioStarted()
        {
            _session.Start(0, 0);

            Assert.AreEqual(SongSessionState.Playing, _session.State);
            Assert.AreEqual(new[] { SongSessionEvent.Started, SongSessionEvent.AudioStarted }, _events.ToArray());
        }

        [Test]
        public void PauseAndResume_FreezeAndContinueTheClock()
        {
            _session.Start(1.0, 0);
            RunFrames(5);

            _session.Pause(PauseReason.User);
            RunFrames(2);
            double frozen = _session.Clock.Frame.SongTime;
            RunFrames(10);

            Assert.AreEqual(SongSessionState.Paused, _session.State);
            Assert.AreEqual(PauseReason.User, _session.PauseReason);
            Assert.AreEqual(frozen, _session.Clock.Frame.SongTime);
            Assert.IsFalse(_session.Clock.Frame.IsRunning);

            _session.Resume();
            RunFrames(20);

            Assert.AreEqual(SongSessionState.LeadIn, _session.State);
            Assert.AreEqual(PauseReason.None, _session.PauseReason);
            Assert.Greater(_session.Clock.Frame.SongTime, frozen);
            Assert.AreEqual(new[] { SongSessionEvent.Started, SongSessionEvent.Paused, SongSessionEvent.Resumed }, _events.ToArray());
        }

        [Test]
        public void Pause_IsIdempotent()
        {
            _session.Start(1.0, 0);
            _session.Pause(PauseReason.User);
            _session.Pause(PauseReason.FocusLost);

            Assert.AreEqual(PauseReason.User, _session.PauseReason);
            Assert.AreEqual(1, _events.FindAll(e => e == SongSessionEvent.Paused).Count);
        }

        [Test]
        public void Reconfigure_WhileRunning_PausesByDeviceAndRecovers()
        {
            _session.Start(1.0, 0);
            RunFrames(3);

            _session.OnEngineClosing();
            Assert.AreEqual(SongSessionState.Recovering, _session.State);
            Assert.AreEqual(PauseReason.DeviceChanged, _session.PauseReason);

            _sampler.Reset();
            _session.OnEngineOpened();
            RunFrames(1);

            Assert.AreEqual(SongSessionState.Paused, _session.State);
            Assert.Contains(SongSessionEvent.Recovered, _events);
        }

        [Test]
        public void ResumeDuringRecovery_ResumesAfterRecovered()
        {
            _session.Start(1.0, 0);
            _session.Pause(PauseReason.User);
            _session.OnEngineClosing();

            _session.Resume();
            Assert.AreEqual(SongSessionState.Recovering, _session.State);

            _sampler.Reset();
            _session.OnEngineOpened();
            RunFrames(1);

            Assert.AreEqual(SongSessionState.LeadIn, _session.State);
            int recovered = _events.IndexOf(SongSessionEvent.Recovered);
            int resumed = _events.IndexOf(SongSessionEvent.Resumed);
            Assert.GreaterOrEqual(recovered, 0);
            Assert.Greater(resumed, recovered);
        }

        [Test]
        public void StopAndDispose_AreTerminal()
        {
            _session.Start(1.0, 0);
            _session.Stop();
            _session.Pause(PauseReason.User);
            Assert.AreEqual(SongSessionState.Stopped, _session.State);

            _session.Dispose();
            _session.Dispose();
            Assert.AreEqual(SongSessionState.Disposed, _session.State);
            Assert.AreEqual(1, _events.FindAll(e => e == SongSessionEvent.Disposed).Count);
        }
    }
}
