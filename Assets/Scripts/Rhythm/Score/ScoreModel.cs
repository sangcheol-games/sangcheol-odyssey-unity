using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정을 받아 점수·콤보·게이지·클리어 등급을 계산한다. 점수는 판정별 개수로 매번 다시 계산해 누적 오차가 없다.
    public sealed class ScoreModel
    {
        // 계산 오차로 정수 바로 아래(699,999.9999…)가 되는 것만 막는다. 소수점은 버린다
        private const double PointTolerance = 1e-6;
        private const int GradeCount = (int)JudgeType.Umm + 1;

        private readonly ScoreRules _rules;
        private readonly int[] _counts = new int[GradeCount];

        public ScoreModel(ScoreRules rules, int totalNotes)
        {
            if (totalNotes < 0) throw new ArgumentException($"총 노트 수는 0 이상이어야 한다: {totalNotes}");

            _rules = rules;
            TotalNotes = totalNotes;
        }

        public ScoreRules Rules => _rules;
        public int TotalNotes { get; }
        public int JudgedCount { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }

        public int CountOf(JudgeType grade) => _counts[(int)grade];

        // 지금까지 받은 판정의 점수(소수 포함). 전부 Master 이상이어도 보너스는 FinalScore에서만 붙는다
        public double Score => TotalNotes > 0 ? _rules.MaxScore * WeightedHits() / TotalNotes : 0;

        public int DisplayScore => ToPoints(Score);

        // 받은 판정만으로 본 달성률(%). 아직 판정이 없으면 100
        public double GaugePercent => JudgedCount > 0 && TotalNotes > 0 ? 100.0 * WeightedHits() / JudgedCount : 100.0;

        public bool IsOverMillion => CountOf(JudgeType.Perfect) + CountOf(JudgeType.Master) == TotalNotes;

        public double ExBonus
            => TotalNotes > 0 ? _rules.MaxScore * _rules.PerfectExBonus * CountOf(JudgeType.Perfect) / TotalNotes : 0;

        // 전부 Master 이상이면 MaxScore + Perfect 보너스, 아니면 표기 점수 그대로
        public int FinalScore => IsOverMillion ? ToPoints(_rules.MaxScore + ExBonus) : DisplayScore;

        public ClearType ClearType
        {
            get
            {
                if (FinalScore < _rules.FailBelowScore) return ClearType.Fail;
                if (CountOf(JudgeType.Perfect) == TotalNotes) return ClearType.AllPerfect;
                if (IsOverMillion) return ClearType.OverMillion;
                if (ComboBreaks() == 0) return ClearType.FullCombo;
                return ClearType.Clear;
            }
        }

        public ScoreRank Rank => _rules.RankOf(FinalScore);

        public void Apply(in JudgeEvent e) => Apply(e.IsMiss ? JudgeType.Umm : e.Judge);

        public void Apply(JudgeType grade)
        {
            _counts[(int)grade]++;
            JudgedCount++;

            if (_rules.BreaksCombo(grade))
            {
                Combo = 0;
            }
            else
            {
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
            }
        }

        public void Reset()
        {
            Array.Clear(_counts, 0, _counts.Length);
            JudgedCount = 0;
            Combo = 0;
            MaxCombo = 0;
        }

        private double WeightedHits()
        {
            double sum = 0;
            for (int i = 0; i < GradeCount; i++)
                sum += _counts[i] * _rules.MultiplierOf((JudgeType)i);
            return sum;
        }

        private int ComboBreaks()
        {
            int sum = 0;
            for (int i = 0; i < GradeCount; i++)
                if (_rules.BreaksCombo((JudgeType)i)) sum += _counts[i];
            return sum;
        }

        private static int ToPoints(double score) => (int)Math.Floor(score + PointTolerance);

        public override string ToString()
            => $"{DisplayScore} (final {FinalScore}, {ClearType}, {Rank}) combo {Combo}/{MaxCombo} " +
               $"P{CountOf(JudgeType.Perfect)} M{CountOf(JudgeType.Master)} I{CountOf(JudgeType.Ideal)} K{CountOf(JudgeType.Kind)} U{CountOf(JudgeType.Umm)} / {TotalNotes}";
    }
}
