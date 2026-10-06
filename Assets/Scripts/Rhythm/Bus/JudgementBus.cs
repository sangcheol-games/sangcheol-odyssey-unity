using System;

namespace SCOdyssey.Rhythm
{
    // 키 입력 1건. 판정 결과와 무관하게 누름/뗌마다 나간다.
    public readonly struct LaneInputEvent
    {
        public readonly Lane Lane;
        public readonly bool IsPressed;
        public readonly double Time;   // 게임 상대 시각(초)

        public LaneInputEvent(Lane lane, bool isPressed, double time)
        {
            Lane = lane;
            IsPressed = isPressed;
            Time = time;
        }

        public override string ToString() => $"{Lane} {(IsPressed ? "P" : "R")} @{Time:F3}";
    }

    // 판정/입력 결과의 구독 창구. ServiceLocator에 등록된다.
    // 그룹·위치 같은 화면 어휘는 싣지 않는다. 받는 쪽이 LaneLayout으로 바꿔 쓴다.
    public interface IJudgementBus
    {
        event Action<LaneInputEvent> LaneInput;   // 판정보다 먼저 나간다
        event Action<JudgeEvent> NoteJudged;      // miss도 온다(IsMiss). 한 번에 여러 개면 Time 순
    }

    // GameManager가 소유하고, RhythmSession이 발행한다.
    public sealed class JudgementBus : IJudgementBus
    {
        public event Action<LaneInputEvent> LaneInput;
        public event Action<JudgeEvent> NoteJudged;

        public void PublishLaneInput(in LaneInputEvent e) => LaneInput?.Invoke(e);
        public void PublishNoteJudged(in JudgeEvent e) => NoteJudged?.Invoke(e);
    }
}
