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

        // 이 누름이 친 노트를 돌려준다. 윈도우 밖이거나 칠 노트가 없으면 NoTarget
        public PressOutcome Press(Lane lane, double time)
        {
            _bus.PublishLaneInput(new LaneKeyEvent(lane, true, time));
            _events.Clear();
            bool hit = _engine.Press(lane, time, _events);
            PublishJudged();

            if (!hit) return PressOutcome.NoTarget;
            // 친 노트는 이 입력 시각의 유일한 적중. 떼지 않은 채 다음 머리를 누르면 앞 홀드 꼬리의 miss가 뒤에 붙는다
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                JudgeEvent e = _events[i];
                if (!e.IsMiss) return new PressOutcome(e.NoteId, e.Kind, e.Judge);
            }
            return PressOutcome.NoTarget;
        }

        public void Release(Lane lane, double time)
        {
            _bus.PublishLaneInput(new LaneKeyEvent(lane, false, time));
            _events.Clear();
            _engine.Release(lane, time, _events);
            PublishJudged();
        }

        // 실제로 뗀 것이 아닌 release(일시정지·포커스 상실·입력 맵 전환으로 생긴 합성 release).
        // 입력 이벤트만 내보내고 엔진은 건드리지 않는다. 홀드 중인 꼬리는 그대로 남아,
        // 다시 잡고 윈도우 안에 떼면 판정되고 아니면 윈도우가 닫힐 때 miss가 된다.
        public void ReleaseUnjudged(Lane lane, double time)
        {
            _bus.PublishLaneInput(new LaneKeyEvent(lane, false, time));
        }

        private void PublishJudged()
        {
            foreach (JudgeEvent e in _events) _bus.PublishNoteJudged(e);
        }
    }
}
