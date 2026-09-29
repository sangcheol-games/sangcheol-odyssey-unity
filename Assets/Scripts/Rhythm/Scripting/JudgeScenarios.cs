using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 판정 규칙을 한 가지씩 보여주는 시나리오 목록. 기본 판정 설정, 60Hz 프레임 기준.
    public static class JudgeScenarios
    {
        private const int Bpm = 120;   // 마디 2초. 4비트면 한 칸 0.5초, 1마디는 2.0초에 시작

        public static readonly IReadOnlyList<JudgeScenario> All = new[]
        {
            new JudgeScenario("taps-grades",
                "레인1 탭 4개를 0/+30/-70/+100ms에 눌러 Perfect/Master/Ideal/Kind. 레인2 탭은 200ms 늦게 눌러 창 밖이라 Miss",
                Chart("#001:01:1111;", "#001:02:1000;"), Bpm,
                new InputScript().Tap(Lane.L1, 2.0).Tap(Lane.L1, 2.53).Tap(Lane.L1, 2.93).Tap(Lane.L1, 3.6).Tap(Lane.L2, 2.2),
                "#0 Normal Perfect", "#1 Normal Miss", "#2 Normal Master", "#3 Normal Ideal", "#4 Normal Kind"),

            new JudgeScenario("earliest-overlap",
                "125ms 간격 탭 두 개. 두 번째에 더 가까운 2.1초에 눌러도 앞 노트를 집는다(Earliest)",
                Chart("#001:01:1100000000000000;"), Bpm,
                new InputScript().Tap(Lane.L1, 2.1, 0.01).Tap(Lane.L1, 2.125),
                "#0 Normal Kind", "#1 Normal Perfect"),

            new JudgeScenario("hold-end-held",
                "끝점(4) 홀드를 끝까지 누르고 있으면 머리와 끝점 모두 Perfect",
                Chart("#001:01:2004;"), Bpm, Auto("#001:01:2004;"),
                "#0 HoldStart Perfect", "#1 HoldEnd Perfect"),

            new JudgeScenario("hold-end-released-early",
                "끝점(4) 홀드를 중간(2.8초)에 떼면 끝점이 Miss",
                Chart("#001:01:2004;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 2.8),
                "#0 HoldStart Perfect", "#1 HoldEnd Miss"),

            new JudgeScenario("hold-release-timing",
                "릴리즈(5) 홀드는 떼는 타이밍으로 판정. 30ms 늦게 떼면 Master",
                Chart("#001:01:2005;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 3.53),
                "#0 HoldStart Perfect", "#1 HoldRelease Master"),

            new JudgeScenario("hold-release-kept-held",
                "릴리즈(5) 홀드를 떼지 않고 계속 누르면 릴리즈가 Miss",
                Chart("#001:01:2005;"), Bpm, new InputScript().Press(Lane.L1, 2.0),
                "#0 HoldStart Perfect", "#1 HoldRelease Miss"),

            new JudgeScenario("hold-head-missed",
                "머리를 창 밖(300ms 늦게)에 눌러 머리는 Miss여도, 제때 떼면 릴리즈(5)는 따로 Perfect",
                Chart("#001:01:2005;"), Bpm, new InputScript().Hold(Lane.L1, 2.3, 3.5),
                "#0 HoldStart Miss", "#1 HoldRelease Perfect"),

            new JudgeScenario("holding-body",
                "본체(3)가 있는 홀드를 끝까지 누르면 전부 Perfect. 종료 문자가 없어 마디 끝(4.0초)에 끝점이 합성된다",
                Chart("#001:01:2030;"), Bpm, Auto("#001:01:2030;"),
                "#0 HoldStart Perfect", "#1 Holding Perfect", "#2 HoldEnd Perfect"),

            new JudgeScenario("holding-body-hitch",
                "같은 홀드를 끝까지 누르고 있어도 2.97초 직후 프레임이 300ms 멈춰 본체(3.0초 ±21ms)를 건너뛰면 본체가 Miss",
                Chart("#001:01:2030;"), Bpm, Auto("#001:01:2030;"),
                "#0 HoldStart Perfect", "#1 Holding Miss", "#2 HoldEnd Perfect").WithHitch(2.97, 0.3),

            new JudgeScenario("hold-kept-into-next-hold",
                "같은 레인 홀드 두 개를 처음부터 계속 누르면 두 번째 머리는 Miss인데 두 번째 끝점은 Perfect",
                Chart("#001:01:20402040;"), Bpm, new InputScript().Press(Lane.L1, 2.0),
                "#0 HoldStart Perfect", "#1 HoldEnd Perfect", "#2 HoldStart Miss", "#3 HoldEnd Perfect"),

            new JudgeScenario("bar-end-synth",
                "마디 끝까지 가는 홀드는 마디 끝(4.0초)에 합성된 끝점까지 누르면 Perfect",
                Chart("#001:01:0020;"), Bpm, Auto("#001:01:0020;"),
                "#0 HoldStart Perfect", "#1 HoldEnd Perfect"),
        };

        private static string Chart(params string[] lines) => string.Join("\n", lines);

        private static InputScript Auto(string chart) => Autoplay.Perfect(ChartParser.Parse(chart, Bpm).BuildJudgeTrack());
    }
}
