using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm.Tests
{
    public class ScoreModelTests
    {
        private static ScoreModel Play(int totalNotes, params (JudgeType grade, int count)[] judged)
            => Play(ScoreRules.Default, totalNotes, judged);

        private static ScoreModel Play(ScoreRules rules, int totalNotes, params (JudgeType grade, int count)[] judged)
        {
            var model = new ScoreModel(rules, totalNotes);
            foreach ((JudgeType grade, int count) in judged)
                for (int i = 0; i < count; i++)
                    model.Apply(grade);
            return model;
        }

        [Test]
        public void Score_UsesExactShareOfMaxScore()
        {
            // 570개 중 569 Perfect + 1 Ideal = 100만 x 569.7 / 570 (정수 나눗셈이면 999,253)
            ScoreModel model = Play(570, (JudgeType.Perfect, 569), (JudgeType.Ideal, 1));

            Assert.That(model.Score, Is.EqualTo(1_000_000 * 569.7 / 570).Within(1e-6));
            Assert.That(model.DisplayScore, Is.EqualTo(999_473));
            Assert.That(model.FinalScore, Is.EqualTo(999_473), "Ideal이 있으면 보정 없음");
        }

        [TestCase(3)]
        [TestCase(7)]
        [TestCase(269)]
        [TestCase(372)]
        [TestCase(424)]
        [TestCase(558)]
        [TestCase(570)]
        public void AllPerfect_ReachesMaxPlusEx(int totalNotes)
        {
            ScoreModel model = Play(totalNotes, (JudgeType.Perfect, totalNotes));

            Assert.That(model.DisplayScore, Is.EqualTo(1_000_000));
            Assert.That(model.FinalScore, Is.EqualTo(1_200_000));
            Assert.That(model.ClearType, Is.EqualTo(ClearType.AllPerfect));
            Assert.That(model.Rank, Is.EqualTo(ScoreRank.SSS));
            Assert.That(model.MaxCombo, Is.EqualTo(totalNotes));
            Assert.That(model.GaugePercent, Is.EqualTo(100.0));
        }

        [TestCase(3)]
        [TestCase(7)]
        [TestCase(269)]
        [TestCase(570)]
        public void AllMaster_OverMillionWithoutEx(int totalNotes)
        {
            ScoreModel model = Play(totalNotes, (JudgeType.Master, totalNotes));

            Assert.That(model.FinalScore, Is.EqualTo(1_000_000));
            Assert.That(model.ClearType, Is.EqualTo(ClearType.OverMillion));
            Assert.That(model.Rank, Is.EqualTo(ScoreRank.SS));
        }

        [Test]
        public void OverMillion_ExCountsOnlyPerfects()
        {
            ScoreModel model = Play(570, (JudgeType.Perfect, 569), (JudgeType.Master, 1));

            // 100만 + 20만 x 569 / 570 = 1,199,649.12
            Assert.That(model.FinalScore, Is.EqualTo(1_199_649));
            Assert.That(model.ClearType, Is.EqualTo(ClearType.OverMillion));
            Assert.That(model.Rank, Is.EqualTo(ScoreRank.SSS));
        }

        [TestCase(3)]
        [TestCase(7)]
        [TestCase(269)]
        [TestCase(372)]
        [TestCase(424)]
        [TestCase(558)]
        [TestCase(570)]
        public void AllIdeal_LandsExactlyOnFailLine(int totalNotes)
        {
            // 0.7 x n / n 이 699,999.999…로 떨어져도 700,000이어야 한다
            ScoreModel model = Play(totalNotes, (JudgeType.Ideal, totalNotes));

            Assert.That(model.FinalScore, Is.EqualTo(700_000));
            Assert.That(model.ClearType, Is.EqualTo(ClearType.FullCombo), "Ideal은 콤보를 끊지 않는다");
            Assert.That(model.Rank, Is.EqualTo(ScoreRank.C));
            Assert.That(model.GaugePercent, Is.EqualTo(70.0).Within(1e-9));
        }

        [Test]
        public void FailLine_IsExclusive()
        {
            ScoreModel atLine = Play(10, (JudgeType.Perfect, 7), (JudgeType.Umm, 3));
            ScoreModel below = Play(10, (JudgeType.Perfect, 6), (JudgeType.Ideal, 1), (JudgeType.Umm, 3));

            Assert.That(atLine.FinalScore, Is.EqualTo(700_000));
            Assert.That(atLine.ClearType, Is.EqualTo(ClearType.Clear));
            Assert.That(below.FinalScore, Is.EqualTo(670_000));
            Assert.That(below.ClearType, Is.EqualTo(ClearType.Fail));
            Assert.That(below.Rank, Is.EqualTo(ScoreRank.F));
        }

        [Test]
        public void FullCombo_KindOrUmmBreaksIt()
        {
            Assert.That(Play(4, (JudgeType.Perfect, 3), (JudgeType.Ideal, 1)).ClearType, Is.EqualTo(ClearType.FullCombo));
            Assert.That(Play(4, (JudgeType.Perfect, 3), (JudgeType.Kind, 1)).ClearType, Is.EqualTo(ClearType.Clear));
            Assert.That(Play(4, (JudgeType.Perfect, 3), (JudgeType.Umm, 1)).ClearType, Is.EqualTo(ClearType.Clear));
        }

        [Test]
        public void Combo_BreaksOnKindAndUmm_KeepsMax()
        {
            var model = new ScoreModel(ScoreRules.Default, 9);
            JudgeType[] sequence =
            {
                JudgeType.Perfect, JudgeType.Master, JudgeType.Ideal, JudgeType.Kind,
                JudgeType.Perfect, JudgeType.Perfect, JudgeType.Umm, JudgeType.Perfect,
            };
            var combos = new int[sequence.Length];
            for (int i = 0; i < sequence.Length; i++)
            {
                model.Apply(sequence[i]);
                combos[i] = model.Combo;
            }

            Assert.That(combos, Is.EqualTo(new[] { 1, 2, 3, 0, 1, 2, 0, 1 }));
            Assert.That(model.MaxCombo, Is.EqualTo(3));
        }

        [Test]
        public void Gauge_IsRatioOverJudgedNotesOnly()
        {
            var model = new ScoreModel(ScoreRules.Default, 100);
            Assert.That(model.GaugePercent, Is.EqualTo(100.0), "판정 전");

            model.Apply(JudgeType.Perfect);
            model.Apply(JudgeType.Ideal);

            Assert.That(model.GaugePercent, Is.EqualTo(85.0).Within(1e-9));
            Assert.That(model.DisplayScore, Is.EqualTo(17_000));
        }

        [Test]
        public void Apply_Event_MissCountsAsUmm()
        {
            var note = new JudgeNote(1.0, Lane.L1, NoteKind.Tap);
            var model = new ScoreModel(ScoreRules.Default, 2);

            model.Apply(JudgeEvent.Hit(0, note, JudgeType.Ideal, 0.05, 1.05));
            model.Apply(JudgeEvent.Miss(1, note, 0.126, 1.126));

            Assert.That(model.CountOf(JudgeType.Ideal), Is.EqualTo(1));
            Assert.That(model.CountOf(JudgeType.Umm), Is.EqualTo(1));
            Assert.That(model.JudgedCount, Is.EqualTo(2));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            ScoreModel model = Play(5, (JudgeType.Perfect, 3), (JudgeType.Umm, 1));

            model.Reset();

            Assert.That(model.JudgedCount, Is.Zero);
            Assert.That(model.Combo, Is.Zero);
            Assert.That(model.MaxCombo, Is.Zero);
            Assert.That(model.DisplayScore, Is.Zero);
            Assert.That(model.CountOf(JudgeType.Perfect), Is.Zero);
        }

        [Test]
        public void ZeroNotes_NoDivisionByZero()
        {
            var model = new ScoreModel(ScoreRules.Default, 0);

            Assert.That(model.Score, Is.Zero);
            Assert.That(model.GaugePercent, Is.EqualTo(100.0));
            Assert.Throws<ArgumentException>(() => new ScoreModel(ScoreRules.Default, -1));
        }

        [TestCase(1_150_000, ScoreRank.SSS)]
        [TestCase(1_149_999, ScoreRank.SS)]
        [TestCase(1_000_000, ScoreRank.SS)]
        [TestCase(999_999, ScoreRank.S)]
        [TestCase(970_000, ScoreRank.S)]
        [TestCase(969_999, ScoreRank.A)]
        [TestCase(900_000, ScoreRank.A)]
        [TestCase(899_999, ScoreRank.B)]
        [TestCase(800_000, ScoreRank.B)]
        [TestCase(799_999, ScoreRank.C)]
        [TestCase(700_000, ScoreRank.C)]
        [TestCase(699_999, ScoreRank.F)]
        [TestCase(0, ScoreRank.F)]
        public void RankOf_EdgesAreInclusive(int finalScore, ScoreRank expected)
        {
            Assert.That(ScoreRules.Default.RankOf(finalScore), Is.EqualTo(expected));
        }

        [Test]
        public void CustomRules_AreApplied()
        {
            var rules = new ScoreRules(2_000_000, 1.0, 0.9, 0.5, 0.25, 0.0,
                kindBreaksCombo: false, ummBreaksCombo: true,
                perfectExBonus: 0.0, failBelowScore: 0, RankThresholds.Default);

            ScoreModel model = Play(rules, 4, (JudgeType.Perfect, 1), (JudgeType.Master, 1), (JudgeType.Kind, 2));

            Assert.That(model.DisplayScore, Is.EqualTo(1_200_000));
            Assert.That(model.Combo, Is.EqualTo(4), "Kind가 콤보를 안 끊는 규칙");
            Assert.That(model.ClearType, Is.EqualTo(ClearType.FullCombo));
        }

        [Test]
        public void Rules_RejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(() => new RankThresholds(1_000_000, 1_150_000, 970_000, 900_000, 800_000, 700_000));
            Assert.Throws<ArgumentException>(() => new ScoreRules(0, 1, 1, 0.7, 0.5, 0, true, true, 0.2, 700_000, RankThresholds.Default));
            Assert.Throws<ArgumentException>(() => new ScoreRules(1_000_000, 1, 1, -0.7, 0.5, 0, true, true, 0.2, 700_000, RankThresholds.Default));
            Assert.Throws<ArgumentException>(() => new ScoreRules(1_000_000, 1, 1, 0.7, 0.5, 0, true, true, -0.2, 700_000, RankThresholds.Default));
        }

        [TestCase("Chart_0001_Normal", 195)]
        [TestCase("Chart_0001_Hard", 195)]
        [TestCase("Chart_0002_Easy", 155)]
        [TestCase("Chart_0002_Normal", 155)]
        [TestCase("Chart_0002_Hard", 155)]
        public void Autoplay_RealCharts_AllPerfectScore(string name, int bpm)
        {
            string text = File.ReadAllText(Path.Combine(Application.dataPath, "Charts", name + ".txt"));
            ChartData chart = ChartParser.Parse(text, bpm);
            JudgeNote[] track = chart.BuildJudgeTrack();
            InputScript script = Autoplay.Perfect(track);
            double end = Math.Max(track[^1].Time, script.LastTime) + 1.0;

            var model = new ScoreModel(ScoreRules.Default, chart.totalNotes);
            foreach (JudgeEvent e in ScriptedRun.Run(track, JudgeSettings.Default, script, FrameSchedule.Uniform(1.0 / 60, end)))
                model.Apply(e);

            Assert.That(model.JudgedCount, Is.EqualTo(chart.totalNotes));
            Assert.That(model.FinalScore, Is.EqualTo(1_200_000));
            Assert.That(model.ClearType, Is.EqualTo(ClearType.AllPerfect));
            Assert.That(model.MaxCombo, Is.EqualTo(chart.totalNotes));
        }
    }
}
