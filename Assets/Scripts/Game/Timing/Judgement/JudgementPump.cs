using System;
using System.Collections.Generic;
using SCOdyssey.Audio;
using UnityEngine;

namespace SCOdyssey.Game.Timing.Judgement
{
    // 한 프레임의 입력과 시간 진행을 판정 클라이언트에 전달한다(FrameBatched).
    //   1. 입력 소스를 비워 복사본에 담는다(전달 도중 Push가 와도 이번 배치에 섞이지 않는다).
    //   2. 입력마다 qpc = min(qpc, 프레임 시각)으로 곡 시각을 구하고 판정 싱크를 적용해 시각순으로 전달한다.
    //   3. Advance(프레임 곡 시각, 판정 시각) → OnFrame(session).
    // 클라이언트가 없거나 Attach 이전 시각의 입력은 버린다. 세션이 Disposed되면 스스로 뗀다.
    internal sealed class JudgementPump
    {
        private const int BatchCapacity = 256;

        private readonly JudgementTimeline _timeline;
        private readonly List<LaneInputEvent> _batch = new List<LaneInputEvent>(BatchCapacity);
        private readonly Action<SongSessionEvent> _onSessionChanged;

        private IJudgementClient _client;
        private ISongSession _session;
        private long _attachQpc;

        public JudgementPump(JudgementTimeline timeline)
        {
            _timeline = timeline;
            _onSessionChanged = OnSessionChanged;
        }

        public bool IsAttached
        {
            get { return _client != null; }
        }

        public IJudgementClient Client
        {
            get { return _client; }
        }

        public ISongSession Session
        {
            get { return _session; }
        }

        // 진단용 누계(타이밍 오버레이, 하네스).
        public int RejectedInputs { get; private set; }
        public int ClampedInputs { get; private set; }      // 입력 시각이 프레임 시각보다 늦어 잘라 낸 수(시각 변환 오차 신호)
        public int DeliveredInputs { get; private set; }
        public int LastBatchCount { get; private set; }

        public void Attach(IJudgementClient client, ISongSession session, long attachQpc)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (session == null) throw new ArgumentNullException(nameof(session));

            Detach();
            _client = client;
            _session = session;
            _attachQpc = attachQpc;
            _timeline.Unlatch();
            _session.Changed += _onSessionChanged;

            // 이미 시작한 세션에 붙었으면 곧바로 래치한다.
            if (session.State != SongSessionState.Ready) _timeline.Latch();
        }

        public void Detach()
        {
            if (_session != null) _session.Changed -= _onSessionChanged;
            _client = null;
            _session = null;
        }

        public void Run(IInputTimestampSource source)
        {
            _batch.Clear();
            if (source != null) source.Drain(_batch);
            LastBatchCount = _batch.Count;

            if (_client == null) return;
            ISongSession session = _session;
            if (session.State == SongSessionState.Disposed)
            {
                Detach();
                return;
            }

            ISongClock clock = session.Clock;
            SongFrame frame = clock.Frame;
            if (frame.IsRunning) _timeline.Latch();

            SortByQpc(_batch);
            for (int i = 0; i < _batch.Count; i++)
            {
                DeliverInput(_batch[i], clock, frame);
                if (_client == null) return;
            }

            IJudgementClient client = _client;
            try
            {
                client.Advance(frame.SongTime, _timeline.ToJudgeTime(frame.SongTime));
                client.OnFrame(session);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (session.State == SongSessionState.Disposed) Detach();
        }

        private void DeliverInput(LaneInputEvent e, ISongClock clock, SongFrame frame)
        {
            if (e.QpcTicks < _attachQpc) return;

            long qpc = e.QpcTicks;
            if (qpc > frame.QpcTicks)
            {
                qpc = frame.QpcTicks;
                ClampedInputs++;
            }

            SongTimePoint point;
            if (!clock.TrySongTimeAt(qpc, out point) || double.IsNaN(point.SongTime))
            {
                RejectedInputs++;
                return;
            }

            // Started를 놓쳤으면 곡 시계가 흐르는 첫 입력에서 래치한다.
            if (point.IsRunning) _timeline.Latch();

            bool judgeable = point.IsRunning && !e.IsSynthetic;
            var input = new JudgedInput(e.Lane, e.IsDown, qpc, point.SongTime, _timeline.ToJudgeTime(point.SongTime), judgeable, point.Epoch);
            DeliveredInputs++;
            try
            {
                _client.OnLaneInput(in input);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OnSessionChanged(SongSessionEvent sessionEvent)
        {
            if (sessionEvent == SongSessionEvent.Started) _timeline.Latch();
            if (sessionEvent == SongSessionEvent.Disposed) Detach();
        }

        // 안정 삽입 정렬. 한 프레임 입력은 몇 개뿐이고 보통 이미 정렬되어 있다.
        private static void SortByQpc(List<LaneInputEvent> events)
        {
            for (int i = 1; i < events.Count; i++)
            {
                LaneInputEvent current = events[i];
                int j = i - 1;
                while (j >= 0 && events[j].QpcTicks > current.QpcTicks)
                {
                    events[j + 1] = events[j];
                    j--;
                }
                events[j + 1] = current;
            }
        }
    }
}
