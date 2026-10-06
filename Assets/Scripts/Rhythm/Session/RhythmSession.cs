using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 판정 엔진을 몰고 결과를 버스로 내보내는 단일 창구. 버스 발행은 여기서만 한다.
    // 입력은 판정 전에 LaneInput부터 나가고, 판정은 엔진이 낸 순서(Time 순)대로 NoteJudged로 나간다.
    public sealed class RhythmSession
    {
        private readonly JudgeEngine _engine;
        private readonly JudgementBus _bus;
        private readonly List<JudgeEvent> _events = new();

        public RhythmSession(JudgeEngine engine, JudgementBus bus)
        {
            _engine = engine;
            _bus = bus;
        }

        public IJudgeStateReader Reader => _engine;
        public bool IsFinished => _engine.IsFinished;

        public void Advance(double time)
        {
            _events.Clear();
            _engine.Advance(time, _events);
            PublishJudged();
        }

        public void Press(Lane lane, double time)
        {
            _bus.PublishLaneInput(new LaneInputEvent(lane, true, time));
            _events.Clear();
            _engine.Press(lane, time, _events);
            PublishJudged();
        }

        public void Release(Lane lane, double time)
        {
            _bus.PublishLaneInput(new LaneInputEvent(lane, false, time));
            _events.Clear();
            _engine.Release(lane, time, _events);
            PublishJudged();
        }

        private void PublishJudged()
        {
            foreach (JudgeEvent e in _events) _bus.PublishNoteJudged(e);
        }
    }
}
