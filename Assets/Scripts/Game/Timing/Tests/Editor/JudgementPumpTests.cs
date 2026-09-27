using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SCOdyssey.Audio;
using SCOdyssey.Game.Timing.Judgement;
using UnityEngine;
using UnityEngine.TestTools;

namespace SCOdyssey.Game.Timing.Tests
{
    public class JudgementPumpTests
    {
        private int _steps;
        private JudgementTimeline _timeline;
        private JudgementPump _pump;
        private FakeSession _session;
        private FakeSource _source;
        private RecordingClient _client;

        [SetUp]
        public void SetUp()
        {
            _steps = 0;
            _timeline = new JudgementTimeline(() => _steps);
            _pump = new JudgementPump(_timeline);
            _session = new FakeSession();
            _session.State = SongSessionState.Playing;
            _source = new FakeSource();
            _client = new RecordingClient();
        }

        [Test]
        public void Inputs_AreDeliveredInQpcOrderBeforeAdvance()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 3000);
            _source.Push(2, true, 1000);
            _source.Push(3, true, 2000);

            _pump.Run(_source);

            Assert.AreEqual(new[] { "input", "input", "input", "advance", "frame" }, _client.Calls.ToArray());
            Assert.AreEqual(2, _client.Inputs[0].Lane);
            Assert.AreEqual(3, _client.Inputs[1].Lane);
            Assert.AreEqual(1, _client.Inputs[2].Lane);
            Assert.AreEqual(5.0, _client.LastAdvanceSongTime, 1e-12);
        }

        [Test]
        public void InputAfterFrameTime_IsClampedToFrame()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 9000);

            _pump.Run(_source);

            Assert.AreEqual(5000, _client.Inputs[0].QpcTicks);
            Assert.AreEqual(5.0, _client.Inputs[0].SongTime, 1e-12);
        }

        [Test]
        public void JudgmentOffset_AppliesToInputAndAdvance()
        {
            _steps = 10;
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 4000);

            _pump.Run(_source);

            Assert.AreEqual(4.0, _client.Inputs[0].SongTime, 1e-12);
            Assert.AreEqual(3.97, _client.Inputs[0].JudgeTime, 1e-12);
            Assert.AreEqual(4.97, _client.LastAdvanceJudgeTime, 1e-12);
        }

        [Test]
        public void EventsBeforeAttach_AreDropped()
        {
            _pump.Attach(_client, _session, 2000);
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 1500);
            _source.Push(2, true, 2500);

            _pump.Run(_source);

            Assert.AreEqual(1, _client.Inputs.Count);
            Assert.AreEqual(2, _client.Inputs[0].Lane);
        }

        [Test]
        public void WithoutClient_InputsAreDrainedAndDiscarded()
        {
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 1000);
            _pump.Run(_source);
            Assert.AreEqual(0, _source.PendingCount);

            _pump.Attach(_client, _session, 0);
            _pump.Run(_source);

            Assert.AreEqual(0, _client.Inputs.Count);
        }

        [Test]
        public void PushDuringDelivery_IsDeliveredNextFrame()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _client.OnInput = input =>
            {
                if (input.Lane == 1) _source.Push(4, false, 4500);
            };
            _source.Push(1, true, 4000);

            _pump.Run(_source);
            Assert.AreEqual(1, _client.Inputs.Count);

            _session.FakeClock.SetFrame(6000, true);
            _pump.Run(_source);
            Assert.AreEqual(2, _client.Inputs.Count);
            Assert.AreEqual(4, _client.Inputs[1].Lane);
        }

        [Test]
        public void SyntheticOrFrozenInputs_AreNotJudgeable()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.RunningFromQpc = 3000;
            _session.FakeClock.SetFrame(5000, true);
            _source.Push(1, true, 2000);            // 곡 시계가 멈춘 구간
            _source.Push(2, false, 4000, true);     // Synthetic
            _source.Push(3, true, 4500);

            _pump.Run(_source);

            Assert.IsFalse(_client.Inputs[0].Judgeable);
            Assert.IsFalse(_client.Inputs[1].Judgeable);
            Assert.IsTrue(_client.Inputs[2].Judgeable);
        }

        [Test]
        public void NaNSongTime_IsRejected()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _session.FakeClock.NaNAtQpc = 4000;
            _source.Push(1, true, 4000);

            _pump.Run(_source);

            Assert.AreEqual(0, _client.Inputs.Count);
            Assert.AreEqual(1, _pump.RejectedInputs);
        }

        [Test]
        public void JudgmentOffset_LatchesOnStartedAndIgnoresLaterChanges()
        {
            _session.State = SongSessionState.Ready;
            _pump.Attach(_client, _session, 0);
            _steps = 5;
            _session.Raise(SongSessionEvent.Started);
            _steps = 9;

            _session.State = SongSessionState.Playing;
            _session.FakeClock.SetFrame(5000, true);
            _pump.Run(_source);

            Assert.AreEqual(5, _timeline.Steps);
        }

        [Test]
        public void JudgmentOffset_FallsBackToFirstRunningFrame()
        {
            _session.State = SongSessionState.Ready;
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(0, false);
            _pump.Run(_source);
            Assert.IsFalse(_timeline.IsLatched);

            _steps = 3;
            _session.State = SongSessionState.Playing;
            _session.FakeClock.SetFrame(1000, true);
            _pump.Run(_source);

            Assert.IsTrue(_timeline.IsLatched);
            Assert.AreEqual(3, _timeline.Steps);
        }

        [Test]
        public void AttachAfterStart_LatchesImmediately()
        {
            _steps = 2;
            _pump.Attach(_client, _session, 0);

            Assert.IsTrue(_timeline.IsLatched);
            Assert.AreEqual(2, _timeline.Steps);
        }

        [Test]
        public void DisposedSession_DetachesClient()
        {
            _pump.Attach(_client, _session, 0);
            _session.Raise(SongSessionEvent.Disposed);

            Assert.IsFalse(_pump.IsAttached);
            Assert.AreEqual(0, _session.SubscriberCount);

            var second = new FakeSession();
            second.State = SongSessionState.Disposed;
            _pump.Attach(_client, second, 0);
            _pump.Run(_source);

            Assert.IsFalse(_pump.IsAttached);
            Assert.AreEqual(0, _client.Calls.Count);
        }

        [Test]
        public void ClientException_DoesNotStopTheFrame()
        {
            _pump.Attach(_client, _session, 0);
            _session.FakeClock.SetFrame(5000, true);
            _client.OnInput = input => throw new InvalidOperationException("client failure");
            _source.Push(1, true, 4000);

            LogAssert.Expect(LogType.Exception, new Regex("client failure"));
            _pump.Run(_source);

            Assert.AreEqual(new[] { "input", "advance", "frame" }, _client.Calls.ToArray());
        }
    }
}
