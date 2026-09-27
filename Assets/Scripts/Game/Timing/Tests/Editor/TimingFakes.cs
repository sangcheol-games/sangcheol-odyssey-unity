using System;
using System.Collections.Generic;
using SCOdyssey.Audio;

namespace SCOdyssey.Game.Timing.Tests
{
    // 곡 시각 = qpc 틱 / 1000. RunningFromQpc 이전 시각은 멈춘 구간으로 본다.
    internal sealed class FakeClock : ISongClock
    {
        public SongFrame Frame { get; set; }
        public long RunningFromQpc;
        public long NaNAtQpc = -1;

        public event Action<ClockDiscontinuity> Discontinuity
        {
            add { }
            remove { }
        }

        public bool TrySongTimeAt(long qpcTicks, out SongTimePoint point)
        {
            double songTime = qpcTicks / 1000.0;
            if (qpcTicks == NaNAtQpc) songTime = double.NaN;
            point = new SongTimePoint(songTime, 1, qpcTicks >= RunningFromQpc);
            return true;
        }

        public void SetFrame(long qpcTicks, bool isRunning)
        {
            Frame = new SongFrame(qpcTicks / 1000.0, qpcTicks, 1, isRunning);
        }
    }

    internal sealed class FakeSession : ISongSession
    {
        public readonly FakeClock FakeClock = new FakeClock();

        public SongSessionState State { get; set; }
        public PauseReason PauseReason { get; set; }
        public bool IsAudioFinished { get; set; }

        public ISongClock Clock
        {
            get { return FakeClock; }
        }

        public event Action<SongSessionEvent> Changed;

        public void Raise(SongSessionEvent sessionEvent)
        {
            if (Changed != null) Changed(sessionEvent);
        }

        public int SubscriberCount
        {
            get
            {
                if (Changed == null) return 0;
                return Changed.GetInvocationList().Length;
            }
        }

        public void Start(double leadInSeconds, int audioOffsetMs) { }
        public void Pause(PauseReason reason) { }
        public void Resume() { }
        public void Stop() { }
    }

    internal sealed class FakeSource : IInputTimestampSource
    {
        private readonly List<LaneInputEvent> _pending = new List<LaneInputEvent>();

        public int PendingCount
        {
            get { return _pending.Count; }
        }

        public void Push(int lane, bool isDown, long qpcTicks, bool isSynthetic = false)
        {
            _pending.Add(new LaneInputEvent(lane, isDown, qpcTicks, isSynthetic));
        }

        public int Drain(List<LaneInputEvent> into)
        {
            int count = _pending.Count;
            into.AddRange(_pending);
            _pending.Clear();
            return count;
        }
    }

    internal sealed class RecordingClient : IJudgementClient
    {
        public readonly List<JudgedInput> Inputs = new List<JudgedInput>();
        public readonly List<string> Calls = new List<string>();
        public double LastAdvanceSongTime;
        public double LastAdvanceJudgeTime;
        public Action<JudgedInput> OnInput;

        public void OnLaneInput(in JudgedInput input)
        {
            Inputs.Add(input);
            Calls.Add("input");
            if (OnInput != null) OnInput(input);
        }

        public void Advance(double songTime, double judgeTime)
        {
            LastAdvanceSongTime = songTime;
            LastAdvanceJudgeTime = judgeTime;
            Calls.Add("advance");
        }

        public void OnFrame(ISongSession session)
        {
            Calls.Add("frame");
        }
    }
}
