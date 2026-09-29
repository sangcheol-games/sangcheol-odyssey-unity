using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SCOdyssey.Rhythm;
using UnityEngine;
using UnityEngine.UIElements;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 샌드박스 화면(UI Toolkit). 러너 상태를 매 프레임 읽어 보여주고, 위젯 조작은 러너 명령으로 넘긴다.
    // 위젯은 키보드 포커스를 받지 않는다(레인 키·Space가 위젯에 먹히지 않게).
    public sealed class SandboxPanel
    {
        private enum Page { List, Judge, Sound, Help }

        private static readonly double[] Speeds = { 0.25, 0.5, 1, 2, 4 };
        private static readonly (string name, Func<Color> color)[] Grades =
        {
            ("Perfect", () => JudgeTimelineElement.GradeColor(JudgeType.Perfect)),
            ("Master", () => JudgeTimelineElement.GradeColor(JudgeType.Master)),
            ("Ideal", () => JudgeTimelineElement.GradeColor(JudgeType.Ideal)),
            ("Kind", () => JudgeTimelineElement.GradeColor(JudgeType.Kind)),
            ("Umm", () => JudgeTimelineElement.GradeColor(JudgeType.Umm)),
            ("Miss", () => JudgeTimelineElement.MissColor),
        };

        private readonly JudgeSandboxRunner _runner;
        private readonly JudgeTimelineElement _timeline = new();
        private readonly List<Button> _sourceButtons = new();
        private readonly List<Button> _inputButtons = new();
        private readonly List<Button> _speedButtons = new();
        private readonly List<Button> _tabButtons = new();
        private readonly List<VisualElement> _pages = new();
        private readonly List<Button> _itemButtons = new();
        private readonly Dictionary<string, Label> _tally = new();
        private readonly List<(Label expected, Label actual)> _expectedRows = new();
        private Page _page = Page.List;

        private readonly Label _status;
        private readonly Label _resultBadge;
        private readonly Button _playButton;

        private readonly Label _listTitle;
        private readonly ScrollView _list;
        private readonly Label _description;
        private readonly VisualElement _autoplayOptions;

        private readonly ListView _eventList;
        private readonly VisualElement _expectedSection;
        private readonly VisualElement _expectedRowsHost;
        private readonly Label _judgeInfo;

        private readonly Toggle _music;
        private readonly Label _soundHint;
        private int _shownEventCount = -1;

        public SandboxPanel(VisualElement root, JudgeSandboxRunner runner, StyleSheet styleSheet, Font font)
        {
            _runner = runner;
            root.Clear();
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
            root.AddToClassList("sandbox");

            // ── 툴바 ──
            var toolbar = Row("toolbar");
            toolbar.Add(Group("모드", Segmented(_sourceButtons, new[] { "시나리오", "채보" }, "seg",
                i => _runner.SetSource((JudgeSandboxRunner.SourceMode)i))));
            toolbar.Add(Group("입력", Segmented(_inputButtons, new[] { "오토플레이", "스크립트", "키보드" }, "seg",
                i => _runner.SetInputMode((JudgeSandboxRunner.InputMode)i))));

            var transport = Row("segmented");
            transport.Add(Btn("처음부터 (R)", _runner.Restart));
            _playButton = Btn("일시정지 (Space)", _runner.TogglePause);
            transport.Add(_playButton);
            transport.Add(Btn("한 칸 (.)", _runner.StepFrame));
            transport.Add(Btn($"히치 {_runner.HitchMs:0}ms (H)", _runner.InjectHitch));
            toolbar.Add(Group("재생", transport));

            toolbar.Add(Group("속도", Segmented(_speedButtons, Speeds.Select(s => $"{s.ToString(CultureInfo.InvariantCulture)}x").ToArray(), "seg",
                i => _runner.SetSpeed(Speeds[i]))));
            root.Add(toolbar);

            // ── 본문: 가운데 타임라인 + 오른쪽 탭 ──
            var body = Row("body");
            root.Add(body);

            var center = Column("center");
            var statusRow = Row("status");
            _status = new Label();
            _status.AddToClassList("status-text");
            statusRow.Add(_status);
            _resultBadge = new Label();
            _resultBadge.AddToClassList("badge");
            statusRow.Add(_resultBadge);
            foreach ((string name, Func<Color> color) in Grades)
            {
                var badge = new Label();
                badge.AddToClassList("badge");
                badge.style.backgroundColor = WithAlpha(color(), 0.28f);
                badge.style.color = color();
                _tally[name] = badge;
                statusRow.Add(badge);
            }
            center.Add(statusRow);
            center.Add(_timeline);
            center.Add(Legend());
            body.Add(center);

            var right = Column("panel", "right");
            right.Add(Segmented(_tabButtons, new[] { "", "판정", "소리", "도움말" }, "tab", i => ShowPage((Page)i)));
            _tabButtons[0].parent.AddToClassList("tabs");
            var pages = Column("pages");
            right.Add(pages);
            body.Add(right);

            // 목록 탭
            var listPage = AddPage(pages, scroll: true);
            _listTitle = SectionTitle("");
            listPage.Add(_listTitle);
            _list = new ScrollView { focusable = false };
            _list.AddToClassList("item-list");
            listPage.Add(_list);
            _description = new Label();
            _description.AddToClassList("desc");
            listPage.Add(_description);
            _autoplayOptions = Column("options");
            _autoplayOptions.Add(SectionTitle("오토플레이 변형"));
            _autoplayOptions.Add(SliderRow("입력 오프셋(ms)", -150, 150, _runner.AutoplayOffsetMs, v => ApplyAutoplay(offset: v)));
            _autoplayOptions.Add(SliderRow("홀드 중간에 떼기 확률", 0, 1, _runner.BreakHoldChance, v => ApplyAutoplay(breakChance: v)));
            _autoplayOptions.Add(SliderRow("탭 빼먹기 확률", 0, 1, _runner.DropTapChance, v => ApplyAutoplay(dropChance: v)));
            _autoplayOptions.Add(SliderRow("시드", 0, 99, _runner.Seed, v => ApplyAutoplay(seed: Mathf.RoundToInt(v)), wholeNumbers: true));
            listPage.Add(_autoplayOptions);

            // 판정 탭
            var judgePage = AddPage(pages, scroll: false);
            judgePage.Add(SectionTitle("판정 기록 (최신이 위)"));
            judgePage.Add(EventRow(header: true));
            _eventList = new ListView
            {
                fixedItemHeight = 20,
                selectionType = SelectionType.None,
                focusable = false,
                makeItem = () => EventRow(header: false),
                bindItem = BindEvent,
            };
            _eventList.AddToClassList("event-list");
            judgePage.Add(_eventList);
            _expectedSection = Column("expected");
            _expectedSection.Add(SectionTitle("시나리오 기대 결과 / 실제"));
            _expectedRowsHost = Column("expected-rows");
            _expectedSection.Add(_expectedRowsHost);
            judgePage.Add(_expectedSection);
            _judgeInfo = Hint("");
            judgePage.Add(_judgeInfo);

            // 소리 탭
            var soundPage = AddPage(pages, scroll: true);
            soundPage.Add(SectionTitle("소리"));
            _music = Check("음악", _runner.MusicOn, _runner.SetMusic);
            soundPage.Add(_music);
            soundPage.Add(Check("타격음", _runner.HitsoundOn, _runner.SetHitsound));
            soundPage.Add(Check("메트로놈", _runner.MetronomeOn, _runner.SetMetronome));
            soundPage.Add(SliderRow("볼륨", 0, 1, _runner.Volume, _runner.SetVolume));
            soundPage.Add(SliderRow("오디오 오프셋(ms, +면 음악이 늦게)", -200, 200, _runner.AudioOffsetMs, _runner.SetAudioOffsetMs));
            _soundHint = Hint("");
            soundPage.Add(_soundHint);
            soundPage.Add(Hint(
                "타격음: 키보드는 누르는 즉시, 오토플레이·스크립트는 입력 시각에 맞춰 예약해서 울린다.\n" +
                "메트로놈: 박마다 울리고 마디 첫 박은 높은 음이다.\n" +
                "박자가 귀와 어긋나 들리면 오디오 오프셋으로 맞춘다(게임 설정의 오디오 오프셋과 같은 뜻)."));

            // 도움말 탭
            var helpPage = AddPage(pages, scroll: true);
            AddHelp(helpPage);

            ShowPage(_page);
        }

        public void OnRestarted()
        {
            bool scenario = _runner.Source == JudgeSandboxRunner.SourceMode.Scenario;
            Select(_sourceButtons, (int)_runner.Source);
            Select(_inputButtons, (int)_runner.Input);
            _tabButtons[(int)Page.List].text = scenario ? "시나리오" : "채보";

            _listTitle.text = scenario ? "시나리오 (판정 규칙을 하나씩 보여줌)" : "채보 (곡 · 난이도)";
            _list.Clear();
            _itemButtons.Clear();
            if (scenario)
            {
                for (int i = 0; i < JudgeScenarios.All.Count; i++)
                {
                    int index = i;
                    _itemButtons.Add(ListItem($"{i + 1}. {JudgeScenarios.All[i].Name}", () => _runner.SelectScenario(index)));
                }
            }
            else
            {
                for (int i = 0; i < _runner.Charts.Count; i++)
                {
                    int index = i;
                    _itemButtons.Add(ListItem(_runner.Charts[i].Label, () => _runner.SelectChart(index)));
                }
            }
            foreach (Button item in _itemButtons) _list.Add(item);
            Select(_itemButtons, scenario ? _runner.ScenarioIndex : _runner.ChartIndex);

            _description.text = scenario
                ? _runner.Scenario.Description + (_runner.Input == JudgeSandboxRunner.InputMode.Keyboard
                    ? "\n\n(키보드 입력이라 시나리오 입력은 쓰지 않는다. PASS/FAIL 없음)"
                    : "\n\n(입력은 시나리오에 정해져 있다. 오토플레이/스크립트 구분 없음)")
                : "";
            _description.style.display = scenario ? DisplayStyle.Flex : DisplayStyle.None;
            _autoplayOptions.style.display = !scenario && _runner.Input == JudgeSandboxRunner.InputMode.Autoplay
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            JudgeWindows w = _runner.Settings.Windows;
            _judgeInfo.text = $"판정 윈도우(ms): Perfect {Ms(w.Perfect)} / Master {Ms(w.Master)} / Ideal {Ms(w.Ideal)} / Kind {Ms(w.Kind)} / Umm {Ms(w.Umm)}" +
                              (scenario ? " (시나리오는 항상 기본값)" : " (Resources/Config/JudgeSettings)");
            _music.SetEnabled(!scenario);
            _soundHint.text = scenario ? "시나리오 모드에는 곡이 없어 음악은 꺼져 있다. 타격음·메트로놈은 쓸 수 있다." : "";
            _soundHint.style.display = scenario ? DisplayStyle.Flex : DisplayStyle.None;

            bool showExpected = scenario && _runner.Input != JudgeSandboxRunner.InputMode.Keyboard;
            _expectedSection.style.display = showExpected ? DisplayStyle.Flex : DisplayStyle.None;
            _expectedRowsHost.Clear();
            _expectedRows.Clear();
            if (showExpected)
            {
                foreach (string expected in _runner.Scenario.Expected)
                {
                    var row = Row("expected-row");
                    var e = new Label(expected);
                    var a = new Label("…");
                    e.AddToClassList("expected-cell");
                    a.AddToClassList("expected-cell");
                    row.Add(e);
                    row.Add(a);
                    _expectedRowsHost.Add(row);
                    _expectedRows.Add((e, a));
                }
            }

            _timeline.Bind(_runner.Judge, _runner.InputMarks);
            _eventList.itemsSource = _runner.Events;
            _eventList.RefreshItems();
            _shownEventCount = -1;
            Refresh();
        }

        public void Refresh()
        {
            IJudgeStateReader judge = _runner.Judge;
            if (judge == null) return;

            string now = double.IsInfinity(judge.Now) ? "-" : judge.Now.ToString("0.000", CultureInfo.InvariantCulture);
            _status.text = $"{_runner.Title}   t = {now}s   {_runner.Speed.ToString(CultureInfo.InvariantCulture)}x{(_runner.Paused ? "   일시정지" : "")}";
            _playButton.text = _runner.Paused ? "재생 (Space)" : "일시정지 (Space)";
            Select(_speedButtons, Array.IndexOf(Speeds, _runner.Speed));

            _resultBadge.text = _runner.Result switch
            {
                "PASS" => "PASS",
                "FAIL" => "FAIL",
                "-" => "끝",
                _ => "진행 중",
            };
            _resultBadge.EnableInClassList("badge--pass", _runner.Result == "PASS");
            _resultBadge.EnableInClassList("badge--fail", _runner.Result == "FAIL");

            List<JudgeEvent> events = _runner.Events;
            if (events.Count != _shownEventCount)
            {
                _shownEventCount = events.Count;
                foreach ((string name, _) in Grades)
                {
                    int count = name == "Miss"
                        ? events.Count(e => e.IsMiss)
                        : events.Count(e => !e.IsMiss && e.Judge.ToString() == name);
                    _tally[name].text = $"{name} {count}";
                }
                _eventList.RefreshItems();
                RefreshExpected(events);
            }

            _timeline.Refresh();
        }

        private void ShowPage(Page page)
        {
            _page = page;
            Select(_tabButtons, (int)page);
            for (int i = 0; i < _pages.Count; i++) _pages[i].style.display = i == (int)page ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement AddPage(VisualElement host, bool scroll)
        {
            VisualElement page = scroll ? new ScrollView { focusable = false } : new VisualElement();
            page.AddToClassList("page");
            host.Add(page);
            _pages.Add(page);
            return page;
        }

        private static void AddHelp(VisualElement page)
        {
            page.Add(SectionTitle("화면 읽는 법"));
            page.Add(Desc(
                "줄 4개가 게임의 레인 1~4(키 Q A ' /)다. 노트는 오른쪽에서 흘러와 흰 세로선(지금)에 닿는 순간이 판정 시각이다. " +
                "직접 치려면 입력을 키보드로 바꾸고, 노트가 흰 선에 닿을 때 그 레인 키를 누른다. 누르고 있는 레인은 이름표가 초록색이 된다."));
            page.Add(Desc(
                "각 줄 아래쪽의 초록 띠는 그 레인 키를 누르고 있던 구간이다. 띠가 시작하는 곳이 누른 순간, 끝나는 곳이 뗀 순간이고, " +
                "누르고 있는 동안에는 흰 선까지 자란다. 홀드 막대와 띠를 나란히 보면 끝까지 잡았는지, 일찍 뗐는지가 보인다."));
            page.Add(Desc(
                "게임 화면에서는 레인 1·2가 위 판정선, 3·4가 아래 판정선에 붙고 판정선이 마디마다 좌우로 움직이지만, " +
                "판정 엔진은 '시각과 레인'만 보므로 여기서는 모든 레인을 같은 직선으로 그린다. 시나리오는 대부분 레인 1(맨 위 줄)만 쓴다."));
            page.Add(Desc("노트 크기: 큰 칸 = 탭·홀드 머리, 중간 칸 = 홀드 꼬리(채보의 4와 5). 머리와 꼬리 사이 막대가 홀드다."));
            page.Add(SectionTitle("홀드 판정"));
            page.Add(Desc(
                "홀드 1개는 판정 2개다. 머리는 누르는 타이밍, 꼬리는 떼는 타이밍으로 판정한다(채보의 4와 5 모두). " +
                "꼬리 윈도우(±126ms)보다 먼저 떼면 그 순간 꼬리가 Miss가 되고 홀드바가 빨갛게 흐려진다. 다시 눌러도 복구되지 않는다. " +
                "머리를 놓치면 꼬리도 같이 Miss이고, 꼬리 윈도우가 지나도록 계속 누르고 있어도 Miss다. 본체(3)는 판정하지 않는다."));

            page.Add(SectionTitle("단축키"));
            page.Add(Desc(
                "Space  재생/일시정지\n" +
                ".  한 칸(1/60초) 진행\n" +
                "H  히치(다음 판정 프레임을 멈춘 만큼 늦춤)\n" +
                "R  처음부터\n" +
                "N / B  다음 / 이전 시나리오·채보\n" +
                "Tab  시나리오 ↔ 채보\n" +
                "Q A ' /  레인 1~4 (입력이 키보드일 때)"));

            page.Add(SectionTitle("채보 모드"));
            page.Add(Desc(
                "음악은 게임처럼 0번(빈) 마디만큼 늦게 시작한다. 배속을 바꾸면 음악도 같은 배율로 빨라진다. " +
                "판정 윈도우는 Resources/Config/JudgeSettings 에셋 값을 쓴다."));

            page.Add(SectionTitle("시나리오 모드"));
            page.Add(Desc(
                "판정 규칙을 하나씩 보여주는 짧은 채보와 정해진 입력이다. 판정 윈도우는 항상 기본값이고, " +
                "끝나면 기대 결과와 비교해 PASS/FAIL을 띄운다(판정 탭). 같은 목록을 EditMode 테스트도 돌린다."));

            page.Add(SectionTitle("판정 프레임"));
            page.Add(Desc(
                "판정 엔진은 화면 프레임과 상관없이 1/60초 간격으로 돈다. 그래서 배속을 올리거나 에디터가 버벅여도 결과가 같다. " +
                "히치는 이 판정 프레임 하나를 늦추는 것이다. 판정은 입력 시각으로 정해지므로 히치가 있어도 결과는 같다."));
        }

        private void RefreshExpected(List<JudgeEvent> events)
        {
            if (_expectedRows.Count == 0) return;
            var actual = new Dictionary<int, string>();
            foreach (JudgeEvent e in events) actual[e.NoteId] = JudgeOutcome.Describe(e);

            for (int i = 0; i < _expectedRows.Count; i++)
            {
                (Label expected, Label shown) = _expectedRows[i];
                bool decided = actual.TryGetValue(i, out string text);
                shown.text = decided ? text : "…";
                bool match = decided && text == expected.text;
                shown.style.color = !decided ? new Color(0.6f, 0.6f, 0.65f) : match ? new Color(0.45f, 0.95f, 0.55f) : JudgeTimelineElement.MissColor;
            }
        }

        private void BindEvent(VisualElement row, int index)
        {
            List<JudgeEvent> events = _runner.Events;
            JudgeEvent e = events[events.Count - 1 - index];   // 최신이 위
            Color color = e.IsMiss ? JudgeTimelineElement.MissColor : JudgeTimelineElement.GradeColor(e.Judge);

            SetCell(row, 0, $"#{e.NoteId}", color);
            SetCell(row, 1, $"레인{(int)e.Lane + 1}", color);
            SetCell(row, 2, e.Time.ToString("0.000", CultureInfo.InvariantCulture), color);
            SetCell(row, 3, e.Kind.ToString(), color);
            SetCell(row, 4, e.IsMiss ? "Miss" : e.Judge.ToString(), color);
            SetCell(row, 5, e.IsMiss ? "-" : (e.DeltaSec * 1000).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture), color);
        }

        private static void SetCell(VisualElement row, int index, string text, Color color)
        {
            var label = (Label)row[index];
            label.text = text;
            label.style.color = color;
        }

        private static VisualElement EventRow(bool header)
        {
            var row = Row("event-row");
            string[] titles = { "id", "레인", "시각(s)", "종류", "등급", "오차(ms)" };
            string[] widths = { "cell-id", "cell-lane", "cell-time", "cell-kind", "cell-grade", "cell-delta" };
            for (int i = 0; i < titles.Length; i++)
            {
                var cell = new Label(header ? titles[i] : "");
                cell.AddToClassList("cell");
                cell.AddToClassList(widths[i]);
                if (header) cell.AddToClassList("cell--header");
                row.Add(cell);
            }
            return row;
        }

        private static VisualElement Legend()
        {
            var legend = Row("legend");
            legend.Add(LegendItem(JudgeTimelineElement.PendingColor, "대기"));
            legend.Add(LegendItem(JudgeTimelineElement.InputBandColor, "키를 누르고 있던 구간(아래쪽 띠: 시작 = 누름, 끝 = 뗌)"));
            legend.Add(LegendItem(JudgeTimelineElement.HoldActiveColor, "누르는 중인 홀드"));
            legend.Add(LegendItem(JudgeTimelineElement.BrokenHoldColor, "끊긴 홀드"));
            return legend;
        }

        private static VisualElement LegendItem(Color color, string text)
        {
            var item = Row("legend-item");
            var chip = new VisualElement();
            chip.AddToClassList("chip");
            chip.style.backgroundColor = color;
            item.Add(chip);
            item.Add(new Label(text));
            return item;
        }

        private void ApplyAutoplay(float? offset = null, float? breakChance = null, float? dropChance = null, int? seed = null)
            => _runner.SetAutoplayOptions(
                offset ?? _runner.AutoplayOffsetMs,
                breakChance ?? _runner.BreakHoldChance,
                dropChance ?? _runner.DropTapChance,
                seed ?? _runner.Seed);

        private static VisualElement SliderRow(string label, float low, float high, float value, Action<float> onChange, bool wholeNumbers = false)
        {
            var row = Column("slider-row");
            var title = new Label();
            title.AddToClassList("slider-label");
            var slider = new Slider(low, high) { value = value, focusable = false };
            void Show(float v) => title.text = $"{label}: {(wholeNumbers ? Mathf.RoundToInt(v).ToString() : v.ToString(high <= 1 ? "0.00" : "0", CultureInfo.InvariantCulture))}";
            Show(value);
            slider.RegisterValueChangedCallback(e =>
            {
                float v = wholeNumbers ? Mathf.Round(e.newValue) : e.newValue;
                Show(v);
                onChange(v);
            });
            row.Add(title);
            row.Add(slider);
            return row;
        }

        private static Toggle Check(string label, bool value, Action<bool> onChange)
        {
            var toggle = new Toggle(label) { value = value, focusable = false };
            toggle.AddToClassList("check");
            toggle.RegisterValueChangedCallback(e => onChange(e.newValue));
            return toggle;
        }

        private static VisualElement Segmented(List<Button> buttons, string[] labels, string itemClass, Action<int> onClick)
        {
            var row = Row("segmented");
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button button = Btn(labels[i], () => onClick(index));
                button.AddToClassList(itemClass);
                buttons.Add(button);
                row.Add(button);
            }
            return row;
        }

        private static void Select(List<Button> buttons, int selected)
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].EnableInClassList("seg--on", i == selected);
        }

        private static Button ListItem(string text, Action onClick)
        {
            Button button = Btn(text, onClick);
            button.AddToClassList("list-item");
            return button;
        }

        private static Button Btn(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text, focusable = false };
            button.AddToClassList("btn");
            return button;
        }

        private static VisualElement Group(string title, VisualElement content)
        {
            var group = Row("group");
            var label = new Label(title);
            label.AddToClassList("group-label");
            group.Add(label);
            group.Add(content);
            return group;
        }

        private static Label SectionTitle(string text)
        {
            var label = new Label(text);
            label.AddToClassList("section-title");
            return label;
        }

        private static Label Desc(string text)
        {
            var label = new Label(text);
            label.AddToClassList("desc");
            return label;
        }

        private static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("hint");
            return label;
        }

        private static VisualElement Row(params string[] classes)
        {
            var element = new VisualElement();
            element.AddToClassList("row");
            foreach (string c in classes) element.AddToClassList(c);
            return element;
        }

        private static VisualElement Column(params string[] classes)
        {
            var element = new VisualElement();
            element.AddToClassList("column");
            foreach (string c in classes) element.AddToClassList(c);
            return element;
        }

        private static string Ms(double sec) => (sec * 1000).ToString("0.#", CultureInfo.InvariantCulture);

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
