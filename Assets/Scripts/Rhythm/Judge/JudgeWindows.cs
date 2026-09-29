using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정 윈도우(초, 판정 시각 기준 ±). 등급 경계는 포함(<=), Umm 바깥 경계는 배타(<).
    public readonly struct JudgeWindows
    {
        public readonly double Perfect;
        public readonly double Master;
        public readonly double Ideal;
        public readonly double Kind;
        public readonly double Umm;

        public static readonly JudgeWindows Default = FromMilliseconds(21, 42, 84, 105, 126);

        public JudgeWindows(double perfect, double master, double ideal, double kind, double umm)
        {
            bool ascending = 0 < perfect && perfect <= master && master <= ideal && ideal <= kind && kind <= umm;
            if (!ascending)
                throw new ArgumentException($"판정 윈도우는 0보다 크고 오름차순이어야 한다: {perfect}/{master}/{ideal}/{kind}/{umm}");

            Perfect = perfect;
            Master = master;
            Ideal = ideal;
            Kind = kind;
            Umm = umm;
        }

        public static JudgeWindows FromMilliseconds(double perfect, double master, double ideal, double kind, double umm)
            => new(perfect / 1000.0, master / 1000.0, ideal / 1000.0, kind / 1000.0, umm / 1000.0);

        // 타이밍 오차(절댓값, 초) -> 등급. Umm 창 밖인지는 호출자가 먼저 거른다
        public JudgeType Grade(double absDelta)
        {
            if (absDelta <= Perfect) return JudgeType.Perfect;
            if (absDelta <= Master) return JudgeType.Master;
            if (absDelta <= Ideal) return JudgeType.Ideal;
            if (absDelta <= Kind) return JudgeType.Kind;
            return JudgeType.Umm;
        }

        public override string ToString()
            => $"{Perfect * 1000:0.###}/{Master * 1000:0.###}/{Ideal * 1000:0.###}/{Kind * 1000:0.###}/{Umm * 1000:0.###}ms";
    }
}
