using System.Collections.Generic;
using NUnit.Framework;
using SCOdyssey.Audio;

namespace SCOdyssey.Game.Timing.Tests
{
    public class GameplayTimingBindingTests
    {
        private FakeSession _session;
        private bool _running;
        private bool _focused;
        private List<JudgedInput> _inputs;
        private List<double> _advances;
        private List<PauseReason> _externalPauses;
        private GameplayTimingBinding _binding;

        private IJudgementClient Client
        {
            get { return _binding; }
        }

        [SetUp]
        public void SetUp()
        {
            _session = new FakeSession();
            _session.State = SongSessionState.Playing;
            _running = true;
            _focused = true;
            _inputs = new List<JudgedInput>();
            _advances = new List<double>();
            _externalPauses = new List<PauseReason>();
            _binding = new GameplayTimingBinding(_session,
                (songTime, judgeTime) => _advances.Add(judgeTime),
                (in JudgedInput input) => _inputs.Add(input),
                () => _running,
                reason => _externalPauses.Add(reason),
                () => _focused);
        }

        private static JudgedInput Input(bool isDown, bool judgeable)
        {
            return new JudgedInput(1, isDown, 0, 1.0, 1.0, judgeable, 1);
        }

        [Test]
        public void NonJudgeablePress_IsDropped_ReleasePasses()
        {
            Client.OnLaneInput(Input(true, false));
            Client.OnLaneInput(Input(false, false));

            Assert.AreEqual(1, _inputs.Count);
            Assert.IsFalse(_inputs[0].IsDown);
        }

        [Test]
        public void NonJudgeableRelease_PassesEvenWhenNotRunning()
        {
            _running = false;
            Client.OnLaneInput(Input(false, false));

            Assert.AreEqual(1, _inputs.Count);
        }

        [Test]
        public void JudgeableInput_OnlyWhileRunning()
        {
            _running = false;
            Client.OnLaneInput(Input(true, true));
            _running = true;
            Client.OnLaneInput(Input(true, true));

            Assert.AreEqual(1, _inputs.Count);
        }

        [Test]
        public void Advance_OnlyWhileRunning()
        {
            _running = false;
            Client.Advance(1.0, 0.9);
            _running = true;
            Client.Advance(2.0, 1.9);

            Assert.AreEqual(new[] { 1.9 }, _advances.ToArray());
        }

        [Test]
        public void ExternalPause_IsNotifiedOncePerPause()
        {
            _session.State = SongSessionState.Paused;
            _session.PauseReason = PauseReason.FocusLost;
            Client.OnFrame(_session);
            _session.State = SongSessionState.Recovering;
            Client.OnFrame(_session);

            _session.State = SongSessionState.Playing;
            Client.OnFrame(_session);
            _session.State = SongSessionState.Paused;
            _session.PauseReason = PauseReason.DeviceChanged;
            Client.OnFrame(_session);

            Assert.AreEqual(new[] { PauseReason.FocusLost, PauseReason.DeviceChanged }, _externalPauses.ToArray());
        }

        [Test]
        public void UserPause_IsNotNotified()
        {
            _session.State = SongSessionState.Paused;
            _session.PauseReason = PauseReason.User;
            Client.OnFrame(_session);

            Assert.AreEqual(0, _externalPauses.Count);
        }

        [Test]
        public void StartOrResumeWithoutFocus_PausesSession()
        {
            _focused = false;
            _session.Raise(SongSessionEvent.Started);
            _session.Raise(SongSessionEvent.Resumed);
            _session.Raise(SongSessionEvent.AudioStarted);

            Assert.AreEqual(new[] { PauseReason.FocusLost, PauseReason.FocusLost }, _session.PauseCalls.ToArray());
        }

        [Test]
        public void StartWithFocus_DoesNotPause()
        {
            _session.Raise(SongSessionEvent.Started);

            Assert.AreEqual(0, _session.PauseCalls.Count);
        }

        [Test]
        public void Dispose_UnsubscribesAndStopsDelivery()
        {
            _binding.Dispose();
            _binding.Dispose();

            Assert.AreEqual(0, _session.SubscriberCount);
            Client.OnLaneInput(Input(false, false));
            Client.Advance(1.0, 1.0);
            Assert.AreEqual(0, _inputs.Count);
            Assert.AreEqual(0, _advances.Count);
        }
    }
}
