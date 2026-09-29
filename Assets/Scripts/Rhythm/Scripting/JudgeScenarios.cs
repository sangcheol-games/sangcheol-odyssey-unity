using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 판정 규칙을 한 가지씩 보여주는 시나리오 목록. 기본 판정 설정, 60Hz 프레임 기준.
    // 홀드는 머리(누르는 타이밍)와 꼬리(떼는 타이밍) 판정 2개다. 채보의 4와 5는 둘 다 꼬리, 3은 판정 대상이 아니다.
    public static class JudgeScenarios
    {
        private const int Bpm = 120;   // 마디 2초. 4비트면 한 칸 0.5초, 1마디는 2.0초에 시작

        public static readonly IReadOnlyList<JudgeScenario> All = new[]
        {
            new JudgeScenario("taps-grades",
                "레인1 탭 4개를 0/+30/-70/+100ms에 눌러 Perfect/Master/Ideal/Kind. 레인2 탭은 200ms 늦게 눌러 창 밖이라 Miss",
                Chart("#001:01:1111;", "#001:02:1000;"), Bpm,
                new InputScript().Tap(Lane.L1, 2.0).Tap(Lane.L1, 2.53).Tap(Lane.L1, 2.93).Tap(Lane.L1, 3.6).Tap(Lane.L2, 2.2),
                "#0 Tap Perfect", "#1 Tap Miss", "#2 Tap Master", "#3 Tap Ideal", "#4 Tap Kind"),

            new JudgeScenario("earliest-overlap",
                "125ms 간격 탭 두 개. 두 번째에 더 가까운 2.1초에 눌러도 앞 노트를 집는다(Earliest)",
                Chart("#001:01:1100000000000000;"), Bpm,
                new InputScript().Tap(Lane.L1, 2.1, 0.01).Tap(Lane.L1, 2.125),
                "#0 Tap Kind", "#1 Tap Perfect"),

            new JudgeScenario("hold-end-held",
                "끝점(4) 홀드를 누르고 있다가 끝점(3.5초)에 떼면 머리와 꼬리 모두 Perfect. 4도 떼는 타이밍으로 판정한다",
                Chart("#001:01:2004;"), Bpm, Auto("#001:01:2004;"),
                "#0 HoldHead Perfect", "#1 HoldTail Perfect"),

            new JudgeScenario("hold-end-released-early",
                "홀드를 중간(2.8초)에 떼면 꼬리 윈도우(3.5초 ±126ms) 전이라 그 순간 꼬리가 Miss(홀드 끊김)",
                Chart("#001:01:2004;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 2.8),
                "#0 HoldHead Perfect", "#1 HoldTail Miss"),

            new JudgeScenario("hold-tail-early-in-window",
                "꼬리 윈도우 안에서 조금 일찍(100ms) 떼면 끊김이 아니라 떼기 판정(Kind)",
                Chart("#001:01:2004;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 3.4),
                "#0 HoldHead Perfect", "#1 HoldTail Kind"),

            new JudgeScenario("hold-release-timing",
                "릴리즈(5) 홀드를 30ms 늦게 떼면 꼬리 Master",
                Chart("#001:01:2005;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 3.53),
                "#0 HoldHead Perfect", "#1 HoldTail Master"),

            new JudgeScenario("hold-release-kept-held",
                "꼬리 윈도우가 지나도록 계속 누르고 있으면 꼬리 Miss",
                Chart("#001:01:2005;"), Bpm, new InputScript().Press(Lane.L1, 2.0),
                "#0 HoldHead Perfect", "#1 HoldTail Miss"),

            new JudgeScenario("hold-head-missed",
                "머리를 창 밖(300ms 늦게)에 누르면 머리 Miss와 함께 꼬리도 Miss. 제때 떼도 살아나지 않는다",
                Chart("#001:01:2005;"), Bpm, new InputScript().Hold(Lane.L1, 2.3, 3.5),
                "#0 HoldHead Miss", "#1 HoldTail Miss"),

            new JudgeScenario("hold-no-recovery",
                "중간(2.6초)에 뗐다가 2.8초에 다시 눌러도 끊긴 홀드는 복구되지 않는다",
                Chart("#001:01:2004;"), Bpm, new InputScript().Hold(Lane.L1, 2.0, 2.6).Hold(Lane.L1, 2.8, 3.5),
                "#0 HoldHead Perfect", "#1 HoldTail Miss"),

            new JudgeScenario("holding-body-ignored",
                "본체(3)는 판정 대상이 아니다. 종료 문자가 없어 마디 끝(4.0초)에 꼬리가 합성되고, 끝까지 누르다 떼면 머리·꼬리 Perfect",
                Chart("#001:01:2030;"), Bpm, Auto("#001:01:2030;"),
                "#0 HoldHead Perfect", "#1 HoldTail Perfect"),

            new JudgeScenario("hold-with-hitch",
                "같은 홀드 도중 2.97초 직후 프레임이 300ms 멈춰도 결과가 같다. 판정은 프레임이 아니라 입력 시각으로 정해진다",
                Chart("#001:01:2030;"), Bpm, Auto("#001:01:2030;"),
                "#0 HoldHead Perfect", "#1 HoldTail Perfect").WithHitch(2.97, 0.3),

            new JudgeScenario("hold-kept-into-next-hold",
                "같은 레인 홀드 두 개를 처음부터 계속 누르면 첫 꼬리는 너무 오래 눌러 Miss, 두 번째 머리는 새로 누르지 않아 Miss(꼬리도 같이)",
                Chart("#001:01:20402040;"), Bpm, new InputScript().Press(Lane.L1, 2.0),
                "#0 HoldHead Perfect", "#1 HoldTail Miss", "#2 HoldHead Miss", "#3 HoldTail Miss"),

            new JudgeScenario("bar-end-synth",
                "마디 끝까지 가는 홀드는 마디 끝(4.0초)에 합성된 꼬리에서 떼면 Perfect",
                Chart("#001:01:0020;"), Bpm, Auto("#001:01:0020;"),
                "#0 HoldHead Perfect", "#1 HoldTail Perfect"),
        };

        private static string Chart(params string[] lines) => string.Join("\n", lines);

        private static InputScript Auto(string chart) => Autoplay.Perfect(ChartParser.Parse(chart, Bpm).BuildJudgeTrack());
    }
}
