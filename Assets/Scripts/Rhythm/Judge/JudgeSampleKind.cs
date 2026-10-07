namespace SCOdyssey.Rhythm
{
    // 판정 이벤트를 타이밍 기록용으로 분류한 것. Skip은 기록하지 않는다.
    public enum JudgeSampleKind : byte
    {
        Skip,     // 입력 없이 끊긴 꼬리(일찍 뗌, 다음 머리에 끊김, 머리 miss로 죽음): 오차가 음수라 통계에 넣지 않는다
        Press,    // 탭·홀드 머리를 친 것
        Release,  // 홀드 꼬리를 뗀 것
        Miss,     // 윈도우가 닫혀 확정된 miss(오차 = +윈도우)
    }

    public static class JudgeSamples
    {
        public static JudgeSampleKind Classify(in JudgeEvent e)
        {
            if (e.IsMiss) return e.DeltaSec >= 0 ? JudgeSampleKind.Miss : JudgeSampleKind.Skip;
            return e.Kind == NoteKind.HoldTail ? JudgeSampleKind.Release : JudgeSampleKind.Press;
        }
    }
}
