using System;
using System.Collections.Generic;
using System.Linq;

namespace SCOdyssey.Rhythm
{
    // 판정 결과를 "#id 종류 등급|Miss" 한 줄로. noteId 순으로 모으면 틱 간격과 무관하게 비교할 수 있다
    public static class JudgeOutcome
    {
        public static string Describe(JudgeEvent e)
            => $"#{e.NoteId} {e.Kind} {(e.IsMiss ? "Miss" : e.Judge.ToString())}";

        public static string[] Summarize(IEnumerable<JudgeEvent> events)
            => events.OrderBy(e => e.NoteId).Select(Describe).ToArray();
    }

    // 채보 조각 + 입력 + 기대 결과. 테스트와 샌드박스가 같은 목록을 쓴다.
    public sealed class JudgeScenario
    {
        public const double FrameStepSec = 1.0 / 60;
        private const double TailSec = 1.0;

        public string Name { get; }
        public string Description { get; }
        public string ChartText { get; }
        public int Bpm { get; }
        public InputScript Script { get; }
        public IReadOnlyList<string> Expected { get; }
        public double HitchAt { get; private set; } = double.NaN;
        public double HitchSec { get; private set; }

        public bool HasHitch => !double.IsNaN(HitchAt);

        public JudgeScenario(string name, string description, string chartText, int bpm, InputScript script, params string[] expected)
        {
            Name = name;
            Description = description;
            ChartText = chartText;
            Bpm = bpm;
            Script = script;
            Expected = expected;
        }

        public JudgeScenario WithHitch(double at, double sec)
        {
            HitchAt = at;
            HitchSec = sec;
            return this;
        }

        public JudgeNote[] BuildTrack(ChartParseReport report = null)
            => ChartParser.Parse(ChartText, Bpm, report).BuildJudgeTrack(report);

        public double EndTime(JudgeNote[] track)
            => Math.Max(track.Length > 0 ? track[^1].Time : 0, Script.LastTime) + TailSec;

        public IEnumerable<double> Frames(double endSec)
            => HasHitch ? FrameSchedule.WithHitch(FrameStepSec, endSec, HitchAt, HitchSec) : FrameSchedule.Uniform(FrameStepSec, endSec);

        public ScenarioResult Run() => Run(JudgeSettings.Default);

        public ScenarioResult Run(JudgeSettings settings)
        {
            JudgeNote[] track = BuildTrack();
            List<JudgeEvent> events = ScriptedRun.Run(track, settings, Script, Frames(EndTime(track)));
            return new ScenarioResult(JudgeOutcome.Summarize(events), Expected);
        }

        public override string ToString() => Name;
    }

    public sealed class ScenarioResult
    {
        public IReadOnlyList<string> Outcomes { get; }
        public IReadOnlyList<string> Expected { get; }
        public bool Passed => FirstMismatch == null;
        public string FirstMismatch { get; }

        public ScenarioResult(IReadOnlyList<string> outcomes, IReadOnlyList<string> expected)
        {
            Outcomes = outcomes;
            Expected = expected;
            FirstMismatch = FindMismatch(outcomes, expected);
        }

        private static string FindMismatch(IReadOnlyList<string> outcomes, IReadOnlyList<string> expected)
        {
            int n = Math.Max(outcomes.Count, expected.Count);
            for (int i = 0; i < n; i++)
            {
                string actual = i < outcomes.Count ? outcomes[i] : "(없음)";
                string wanted = i < expected.Count ? expected[i] : "(없음)";
                if (actual != wanted) return $"{i}번째: 기대 {wanted}, 실제 {actual}";
            }
            return null;
        }
    }
}
