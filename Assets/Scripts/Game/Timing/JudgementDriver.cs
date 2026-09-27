using System;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing.Diagnostics;
using SCOdyssey.Game.Timing.Judgement;
using UnityEngine;

namespace SCOdyssey.Game.Timing
{
    // 판정 타이밍의 프레임 구동(Game/Timing의 유일한 MonoBehaviour). AudioEngineRunner(-1010)가 곡 시계 프레임을 만든 뒤(-900) 돈다.
    // 매 프레임 입력 소스를 비우고(클라이언트가 없어도) 붙은 클라이언트에 입력 → Advance → OnFrame을 전달한다.
    // 판정 싱크(Func<int>)를 적용하는 타임라인과 판정 기록을 소유하고, 타이밍 오버레이를 OnGUI에서 그린다.
    [DefaultExecutionOrder(AudioExecutionOrder.JudgementDriver)]
    [DisallowMultipleComponent]
    public sealed class JudgementDriver : MonoBehaviour
    {
        private JudgementTimeline _timeline;
        private JudgementPump _pump;
        private TimingLog _log;
        private IInputTimestampSource _source;

        public static bool OverlayVisible { get; set; }

        public IInputTimestampSource Source
        {
            get { return _source; }
        }

        public TimingLog TimingLog
        {
            get { return _log; }
        }

        public bool IsAttached
        {
            get { return _pump != null && _pump.IsAttached; }
        }

        public bool IsJudgmentOffsetLatched
        {
            get { return _timeline != null && _timeline.IsLatched; }
        }

        public int LatchedJudgmentOffsetSteps
        {
            get
            {
                if (_timeline == null) return 0;
                return _timeline.Steps;
            }
        }

        // 진단용 누계(타이밍 오버레이, 하네스).
        public int ClampedInputs
        {
            get { return _pump.ClampedInputs; }
        }

        public int RejectedInputs
        {
            get { return _pump.RejectedInputs; }
        }

        public int DeliveredInputs
        {
            get { return _pump.DeliveredInputs; }
        }

        public int LastBatchCount
        {
            get { return _pump.LastBatchCount; }
        }

        // judgmentOffsetSteps는 세션마다 한 번만 읽는다(Started 또는 첫 진행 프레임).
        public static JudgementDriver Install(GameObject host, IInputTimestampSource source, Func<int> judgmentOffsetSteps)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            JudgementDriver driver = host.GetComponent<JudgementDriver>();
            if (driver == null) driver = host.AddComponent<JudgementDriver>();
            driver.Configure(source, judgmentOffsetSteps);
            return driver;
        }

        private void Configure(IInputTimestampSource source, Func<int> judgmentOffsetSteps)
        {
            if (_pump != null) _pump.Detach();
            _source = source;
            _timeline = new JudgementTimeline(judgmentOffsetSteps);
            _pump = new JudgementPump(_timeline);
            _log = new TimingLog(_timeline, ReadEpoch);
            enabled = true;
        }

        // 붙인 시각 이전의 입력은 버린다. 다른 클라이언트가 붙어 있으면 떼고 바꾼다.
        public void Attach(IJudgementClient client, ISongSession session)
        {
            if (_pump == null) throw new InvalidOperationException("JudgementDriver.Install을 먼저 불러야 합니다.");
            _pump.Attach(client, session, Qpc.Now);
        }

        // 지금 붙어 있는 클라이언트일 때만 뗀다.
        public void Detach(IJudgementClient client)
        {
            if (_pump == null || client == null) return;
            if (ReferenceEquals(_pump.Client, client)) _pump.Detach();
        }

        private void Update()
        {
            if (_pump == null) return;
            _pump.Run(_source);
        }

        private void OnDestroy()
        {
            if (_pump != null) _pump.Detach();
        }

        private void OnGUI()
        {
            if (!OverlayVisible || _pump == null) return;
            TimingOverlay.Draw(this);
        }

        private int ReadEpoch()
        {
            ISongSession session = _pump.Session;
            if (session == null) return 0;
            return session.Clock.Frame.Epoch;
        }
    }
}
