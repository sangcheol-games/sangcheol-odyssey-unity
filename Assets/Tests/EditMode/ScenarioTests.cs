using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SCOdyssey.Rhythm.Tests
{
    // 샌드박스와 같은 시나리오 목록을 기본 판정 설정·60Hz로 돌린다
    public class ScenarioTests
    {
        private static IEnumerable<string> Names => JudgeScenarios.All.Select(s => s.Name);

        [TestCaseSource(nameof(Names))]
        public void Scenario_MatchesExpected(string name)
        {
            JudgeScenario scenario = JudgeScenarios.All.Single(s => s.Name == name);

            ScenarioResult result = scenario.Run();

            Assert.That(result.Outcomes, Is.EqualTo(scenario.Expected), scenario.Description);
        }

        [Test]
        public void Scenarios_HaveUniqueNames_AndCleanCharts()
        {
            Assert.That(Names, Is.Unique);

            foreach (JudgeScenario scenario in JudgeScenarios.All)
            {
                var report = new ChartParseReport();
                JudgeNote[] track = scenario.BuildTrack(report);
                Assert.That(report.Errors, Is.Empty, scenario.Name);
                Assert.That(scenario.Expected.Count, Is.EqualTo(track.Length), $"{scenario.Name}: 노트마다 기대 결과가 하나씩");
            }
        }
    }
}
