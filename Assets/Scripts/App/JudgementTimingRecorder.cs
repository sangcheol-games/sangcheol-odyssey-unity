using System;
using SCOdyssey.Game.Timing;
using SCOdyssey.Rhythm;

namespace SCOdyssey.App
{
    // 판정 오차를 IJudgementTimingLog에 기록한다. 버스(NoteJudged)의 판정 하나가 샘플 하나다.
    // 누름/뗌/miss만 기록하고, 입력 없이 끊긴 꼬리(JudgeSampleKind.Skip, 오차가 음수)는 버린다. 오차는 + = 늦음
    public sealed class JudgementTimingRecorder : IDisposable
    {
        private readonly IJudgementBus _bus;
        private readonly IJudgementTimingLog _log;

        public JudgementTimingRecorder(IJudgementBus bus, IJudgementTimingLog log)
        {
            _bus = bus;
            _log = log;
            _bus.NoteJudged += OnNoteJudged;
        }

        public void Dispose()
        {
            _bus.NoteJudged -= OnNoteJudged;
        }

        private void OnNoteJudged(JudgeEvent e)
        {
            if (_log == null) return;
            if (!TryKindOf(JudgeSamples.Classify(e), out TimingKind kind)) return;

            _log.Record(kind, (int)e.Judge, e.DeltaSec * 1000.0);
        }

        // Rhythm 어셈블리의 분류를 타이밍 로그 어휘로 옮긴다. HoldBody는 엔진에 없다(본체 3은 판정하지 않는다)
        private static bool TryKindOf(JudgeSampleKind sample, out TimingKind kind)
        {
            switch (sample)
            {
                case JudgeSampleKind.Press: kind = TimingKind.Press; return true;
                case JudgeSampleKind.Release: kind = TimingKind.Release; return true;
                case JudgeSampleKind.Miss: kind = TimingKind.Miss; return true;
                default: kind = default; return false;
            }
        }
    }
}
