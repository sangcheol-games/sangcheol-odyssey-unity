using System;

namespace SCOdyssey.Rhythm
{
    // 한 레인에서 윈도우가 겹친 노트가 여럿일 때 입력이 어느 노트를 집는가
    public enum NoteSelectPolicy
    {
        Earliest,   // 가장 이른 노트
        Nearest,    // 입력 시각에 가장 가까운 노트. 같으면 이른 쪽
    }

    public readonly struct JudgeSettings
    {
        public readonly JudgeWindows Windows;
        public readonly NoteSelectPolicy Select;
        public readonly double OffsetSec;         // 유저 판정 오프셋. +면 윈도우 중심이 늦어진다
        public readonly double TailWindowScale;   // 홀드 꼬리(떼기)를 받아주는 폭 = Umm x 이 값. 등급 경계는 그대로

        public static readonly JudgeSettings Default = new(JudgeWindows.Default);

        public JudgeSettings(JudgeWindows windows, NoteSelectPolicy select = NoteSelectPolicy.Earliest, double offsetSec = 0, double tailWindowScale = 1.0)
        {
            if (!(tailWindowScale > 0)) throw new ArgumentException($"꼬리 윈도우 배율은 0보다 커야 한다: {tailWindowScale}");

            Windows = windows;
            Select = select;
            OffsetSec = offsetSec;
            TailWindowScale = tailWindowScale;
        }

        public double TailWindow => Windows.Umm * TailWindowScale;

        public JudgeSettings WithOffset(double offsetSec) => new(Windows, Select, offsetSec, TailWindowScale);
    }
}
