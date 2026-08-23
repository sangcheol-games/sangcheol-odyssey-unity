using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정/입력 결과의 구독 창구. ServiceLocator에 등록된다.
    public interface IJudgementBus
    {
        event Action<JudgeType, NotePosition, LaneGroup> NoteJudged;
        event Action NoteMissed;                                    // 점수만 필요. 연출 없음
        event Action<NotePosition, LaneGroup> HoldStarted;
        event Action<NotePosition, LaneGroup> HoldEnded;
        event Action<NotePosition, LaneGroup> HoldReleased;
        event Action<NotePosition, LaneGroup> LaneInput;            // 판정 결과와 무관한 키 입력
    }

    // GameManager가 소유하고, ChartManager(판정)와 GameManager(입력)가 발행한다.
    public sealed class JudgementBus : IJudgementBus
    {
        public event Action<JudgeType, NotePosition, LaneGroup> NoteJudged;
        public event Action NoteMissed;
        public event Action<NotePosition, LaneGroup> HoldStarted;
        public event Action<NotePosition, LaneGroup> HoldEnded;
        public event Action<NotePosition, LaneGroup> HoldReleased;
        public event Action<NotePosition, LaneGroup> LaneInput;

        public void PublishNoteJudged(JudgeType judge, NotePosition pos, LaneGroup group)
            => NoteJudged?.Invoke(judge, pos, group);

        public void PublishNoteMissed() => NoteMissed?.Invoke();

        public void PublishHoldStarted(NotePosition pos, LaneGroup group) => HoldStarted?.Invoke(pos, group);
        public void PublishHoldEnded(NotePosition pos, LaneGroup group) => HoldEnded?.Invoke(pos, group);
        public void PublishHoldReleased(NotePosition pos, LaneGroup group) => HoldReleased?.Invoke(pos, group);
        public void PublishLaneInput(NotePosition pos, LaneGroup group) => LaneInput?.Invoke(pos, group);
    }
}
