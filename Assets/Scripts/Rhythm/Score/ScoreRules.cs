using System;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 최종 점수 -> 랭크. 각 값은 그 랭크의 최저 점수(이상이면 해당)
    public readonly struct RankThresholds
    {
        public readonly int SSS;
        public readonly int SS;
        public readonly int S;
        public readonly int A;
        public readonly int B;
        public readonly int C;

        public static readonly RankThresholds Default = new(1_150_000, 1_000_000, 970_000, 900_000, 800_000, 700_000);

        public RankThresholds(int sss, int ss, int s, int a, int b, int c)
        {
            bool descending = sss >= ss && ss >= s && s >= a && a >= b && b >= c;
            if (!descending)
                throw new ArgumentException($"랭크 경계는 내림차순이어야 한다: {sss}/{ss}/{s}/{a}/{b}/{c}");

            SSS = sss;
            SS = ss;
            S = s;
            A = a;
            B = b;
            C = c;
        }

        public ScoreRank RankOf(int finalScore)
        {
            if (finalScore >= SSS) return ScoreRank.SSS;
            if (finalScore >= SS) return ScoreRank.SS;
            if (finalScore >= S) return ScoreRank.S;
            if (finalScore >= A) return ScoreRank.A;
            if (finalScore >= B) return ScoreRank.B;
            if (finalScore >= C) return ScoreRank.C;
            return ScoreRank.F;
        }

        public override string ToString() => $"{SSS}/{SS}/{S}/{A}/{B}/{C}";
    }

    // 점수 계산 규칙. 노트당 배점 = MaxScore / 총 노트 수, 판정마다 등급 배율을 곱해 더한다.
    public readonly struct ScoreRules
    {
        public readonly int MaxScore;
        public readonly double PerfectMul;
        public readonly double MasterMul;
        public readonly double IdealMul;
        public readonly double KindMul;
        public readonly double UmmMul;
        public readonly bool KindBreaksCombo;
        public readonly bool UmmBreaksCombo;
        public readonly double PerfectExBonus;   // 전부 Master 이상일 때 Perfect 1개당 노트 배점 x 이 값을 더 준다
        public readonly int FailBelowScore;      // 최종 점수가 이 값 미만이면 Fail
        public readonly RankThresholds Ranks;

        public static readonly ScoreRules Default = new(1_000_000, 1.0, 1.0, 0.7, 0.5, 0.0, true, true, 0.2, 700_000, RankThresholds.Default);

        public ScoreRules(int maxScore,
            double perfectMul, double masterMul, double idealMul, double kindMul, double ummMul,
            bool kindBreaksCombo, bool ummBreaksCombo,
            double perfectExBonus, int failBelowScore, RankThresholds ranks)
        {
            if (maxScore <= 0) throw new ArgumentException($"최대 점수는 0보다 커야 한다: {maxScore}");
            bool validMuls = perfectMul >= 0 && masterMul >= 0 && idealMul >= 0 && kindMul >= 0 && ummMul >= 0;
            if (!validMuls)
                throw new ArgumentException($"배율은 0 이상이어야 한다: {perfectMul}/{masterMul}/{idealMul}/{kindMul}/{ummMul}");
            if (!(perfectExBonus >= 0)) throw new ArgumentException($"Perfect 보너스는 0 이상이어야 한다: {perfectExBonus}");

            MaxScore = maxScore;
            PerfectMul = perfectMul;
            MasterMul = masterMul;
            IdealMul = idealMul;
            KindMul = kindMul;
            UmmMul = ummMul;
            KindBreaksCombo = kindBreaksCombo;
            UmmBreaksCombo = ummBreaksCombo;
            PerfectExBonus = perfectExBonus;
            FailBelowScore = failBelowScore;
            Ranks = ranks;
        }

        public double MultiplierOf(JudgeType grade) => grade switch
        {
            JudgeType.Perfect => PerfectMul,
            JudgeType.Master => MasterMul,
            JudgeType.Ideal => IdealMul,
            JudgeType.Kind => KindMul,
            _ => UmmMul,
        };

        public bool BreaksCombo(JudgeType grade) => grade switch
        {
            JudgeType.Kind => KindBreaksCombo,
            JudgeType.Umm => UmmBreaksCombo,
            _ => false,
        };

        public ScoreRank RankOf(int finalScore) => Ranks.RankOf(finalScore);

        public override string ToString()
            => $"max {MaxScore}, x{PerfectMul}/{MasterMul}/{IdealMul}/{KindMul}/{UmmMul}, ex {PerfectExBonus}, fail<{FailBelowScore}, rank {Ranks}";
    }
}
