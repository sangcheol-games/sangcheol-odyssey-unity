using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SCOdyssey.App;
using SCOdyssey.Config;
using SCOdyssey.Domain.Entity;
using SCOdyssey.Rhythm;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 엔진만 돌려 보는 샌드박스. Managers 없이 동작한다.
    // 엔진에 쓰는 건 이 러너뿐이고, 타임라인(JudgeTimelineElement)은 IJudgeStateReader만 읽는다.
    //
    // 엔진은 화면 프레임이 아니라 고정 60Hz 판정 프레임으로 돈다(FrameStepper). 시계는 판정 프레임을 얼마나 빨리
    // 내보낼지만 정하므로, 에디터 프레임레이트와 무관하게 테스트(ScriptedRun)와 같은 결과가 나온다.
    // 소리(SandboxAudio)는 시계를 따라간다. 일시정지·배속·건너뛰기 때마다 Reanchor로 다시 맞춘다.
    public sealed class JudgeSandboxRunner : MonoBehaviour
    {
        public enum SourceMode { Scenario, Chart }
        public enum InputMode { Autoplay, Script, Keyboard }

        public sealed class ChartEntry
        {
            public string Label;
            public TextAsset Chart;
            public int Bpm;
            public string AudioFile;
        }

        private const double StepSec = JudgeScenario.FrameStepSec;
        private const double MaxRealFrameSec = 0.25;  // 에디터가 멈췄다 돌아오면 시계는 여기까지만 흐르고 소리는 다시 맞춘다
        private const double TailSec = 1.0;
        private const double ClickLookaheadSec = 0.2;
        private const double DriftCheckSec = 1.0;
        private const double MaxDriftSec = 0.06;

        [Header("무엇을 돌릴까")]
        [SerializeField] private SourceMode source = SourceMode.Scenario;
        [SerializeField] private int scenarioIndex;
        [SerializeField] private int chartIndex;

        [Header("입력 (시나리오는 Keyboard가 아니면 시나리오에 딸린 입력을 쓴다)")]
        [SerializeField] private InputMode inputMode = InputMode.Autoplay;
        [Tooltip("한 줄에 \"시각(초) 레인(1~4) P|R\", #부터 주석")]
        [SerializeField] private TextAsset inputScript;
        [SerializeField] private float autoplayOffsetMs;
        [SerializeField, Range(0f, 1f)] private float breakHoldChance;
        [SerializeField, Range(0f, 1f)] private float dropTapChance;
        [SerializeField] private int seed = 1;

        [Header("판정 설정 (채보 모드. 비우면 Resources/Config/JudgeSettings, 시나리오는 항상 기본값)")]
        [SerializeField] private JudgeSettingsSO judgeSettings;

        [Header("시계")]
        [SerializeField] private float speed = 1f;
        [SerializeField] private float hitchMs = 300f;
        [SerializeField] private float startAtSec = -1f;
        [SerializeField] private bool startPaused;

        [Header("소리")]
        [SerializeField] private bool musicOn = true;
        [SerializeField] private bool hitsoundOn = true;
        [SerializeField] private bool metronomeOn;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [Tooltip("양수면 음악이 늦게 나온다(게임 설정의 오디오 오프셋과 같은 뜻)")]
        [SerializeField] private float audioOffsetMs;

        [Header("화면")]
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private Font font;

        [Header("시작할 때 시나리오 전체를 화면 없이 돌려 SUMMARY 로그")]
        [SerializeField] private bool runAllScenariosOnStart = true;

        private readonly ManualClock _clock = new();
        private readonly List<JudgeEvent> _frameEvents = new();
        private readonly List<JudgeEvent> _events = new();
        private readonly List<ScriptedInput> _inputMarks = new();
        private readonly Queue<(Lane lane, bool isPress, double realtime)> _keyQueue = new();
        private readonly Queue<ScriptedInput> _pendingKeys = new();
        private readonly List<ChartEntry> _charts = new();

        private SandboxAudio _audio;
        private SandboxPanel _panel;
        private InputManager _keyboard;
        private JudgeEngine _engine;
        private JudgeSettings _settings;
        private InputScript _script;
        private ScriptCursor _cursor;
        private FrameStepper _stepper;
        private JudgeScenario _scenario;
        private string _title = "";
        private string _result = "";
        private double _endTime;
        private double _prevRealtime;
        private double _beatSec;
        private double _musicStartClock;
        private int _clickInput;
        private long _nextBeat;
        private double _nextDriftCheck;
        private bool _summaryLogged;

        public SourceMode Source => source;
        public InputMode Input => inputMode;
        public int ScenarioIndex => scenarioIndex;
        public int ChartIndex => chartIndex;
        public IReadOnlyList<ChartEntry> Charts => _charts;
        public JudgeScenario Scenario => _scenario;
        public IJudgeStateReader Judge => _engine;
        public List<JudgeEvent> Events => _events;
        public IReadOnlyList<ScriptedInput> InputMarks => _inputMarks;
        public JudgeSettings Settings => _settings;
        public string Title => _title;
        public string Result => _result;
        public bool Paused => _clock.Paused;
        public double Speed => _clock.Speed;
        public bool MusicOn => musicOn;
        public bool HitsoundOn => hitsoundOn;
        public bool MetronomeOn => metronomeOn;
        public float Volume => volume;
        public float AudioOffsetMs => audioOffsetMs;
        public float AutoplayOffsetMs => autoplayOffsetMs;
        public float BreakHoldChance => breakHoldChance;
        public float DropTapChance => dropTapChance;
        public int Seed => seed;
        public float HitchMs => hitchMs;
        public bool HasMusic => _audio != null && _audio.HasMusic;

        private void Start()
        {
            if (runAllScenariosOnStart) RunAllScenariosHeadless();
            LoadCharts();

            _audio = new SandboxAudio();
            _audio.SetVolume(volume);

            _keyboard = new InputManager();
            _keyboard.SetTimeSyncPoint(0, 0);   // 콜백 시각을 realtime 그대로 받는다
            _keyboard.OnLanePressed += (lane, realtime) => OnKey(lane, true, realtime);
            _keyboard.OnLaneReleased += (lane, realtime) => OnKey(lane, false, realtime);
            _keyboard.SwitchToGameplay();

            if (document != null) _panel = new SandboxPanel(document.rootVisualElement, this, styleSheet, font);
            Restart();
        }

        private void OnDestroy()
        {
            _keyboard?.Disable();
            _audio?.Dispose();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (_audio == null || _engine == null || Application.runInBackground) return;
            if (hasFocus) Reanchor();
            else _audio.Anchor(_clock.Now, _clock.Speed, false, false, _musicStartClock);   // 멈춘 동안 소리만 앞서가지 않게
        }

        private void Update()
        {
            HandleShortcuts();
            if (_engine == null) return;

            double realDt = Time.unscaledDeltaTime;
            double prevClock = _clock.Now;
            double prevReal = _prevRealtime;
            _clock.Tick(Math.Min(realDt, MaxRealFrameSec));
            _prevRealtime = Time.realtimeSinceStartupAsDouble;
            if (realDt > MaxRealFrameSec) Reanchor();

            QueueKeyboard(prevReal, _prevRealtime, prevClock, _clock.Now);
            while (_stepper.TryTake(_clock.Now, out double frame)) StepEngine(frame);

            ScheduleClicks();
            CheckMusicDrift();

            if (!_summaryLogged && _engine.IsFinished && _cursor.IsDone && _engine.Now >= _endTime) LogSummary();
            _panel?.Refresh();
        }

        // ---------------- 위젯/단축키가 부르는 명령 ----------------

        public void SetSource(SourceMode mode)
        {
            source = mode;
            Restart();
        }

        public void SelectScenario(int index)
        {
            source = SourceMode.Scenario;
            scenarioIndex = index;
            Restart();
        }

        public void SelectChart(int index)
        {
            source = SourceMode.Chart;
            chartIndex = index;
            Restart();
        }

        public void SetInputMode(InputMode mode)
        {
            inputMode = mode;
            Restart();
        }

        public void SetAutoplayOptions(float offsetMs, float breakChance, float dropChance, int newSeed)
        {
            autoplayOffsetMs = offsetMs;
            breakHoldChance = breakChance;
            dropTapChance = dropChance;
            seed = newSeed;
            if (source == SourceMode.Chart && inputMode == InputMode.Autoplay) Restart();
        }

        public void TogglePause()
        {
            _clock.Paused = !_clock.Paused;
            Reanchor();
        }

        public void StepFrame()
        {
            _clock.Jump(StepSec);
            _clock.Tick(0);
            Reanchor();
        }

        public void InjectHitch()
        {
            if (_engine != null) _stepper.DelayNext(_engine.Now, hitchMs / 1000.0);
        }

        public void SetSpeed(double newSpeed)
        {
            speed = (float)newSpeed;
            _clock.Speed = newSpeed;
            Reanchor();
        }

        public void SetMusic(bool on)
        {
            musicOn = on;
            Reanchor();
        }

        public void SetHitsound(bool on)
        {
            hitsoundOn = on;
            Reanchor();
        }

        public void SetMetronome(bool on)
        {
            metronomeOn = on;
            Reanchor();
        }

        public void SetVolume(float value)
        {
            volume = value;
            _audio?.SetVolume(value);
        }

        public void SetAudioOffsetMs(float ms)
        {
            audioOffsetMs = ms;
            UpdateMusicStart();
            Reanchor();
        }

        public void Restart()
        {
            JudgeNote[] track;
            _scenario = null;
            int bpm;

            if (source == SourceMode.Scenario)
            {
                int count = JudgeScenarios.All.Count;
                scenarioIndex = ((scenarioIndex % count) + count) % count;
                _scenario = JudgeScenarios.All[scenarioIndex];
                track = _scenario.BuildTrack();
                _settings = JudgeSettings.Default;
                _script = inputMode == InputMode.Keyboard ? new InputScript() : _scenario.Script;
                bpm = _scenario.Bpm;
                _title = _scenario.Name;
                _audio?.UnloadMusic();
                Debug.Log($"[JudgeSandbox] {_scenario.Name}: {_scenario.Description}");
            }
            else
            {
                if (_charts.Count == 0)
                {
                    Debug.LogError("[JudgeSandbox] Resources/Music에서 채보를 찾지 못했다");
                    _engine = null;
                    return;
                }

                chartIndex = ((chartIndex % _charts.Count) + _charts.Count) % _charts.Count;
                ChartEntry entry = _charts[chartIndex];
                var report = new ChartParseReport();
                track = ChartParser.Parse(entry.Chart.text, entry.Bpm, report).BuildJudgeTrack(report);
                foreach (string error in report.Errors) Debug.LogError(error);
                _settings = JudgeSettingsSO.Resolve(judgeSettings);
                _script = BuildChartScript(track);
                bpm = entry.Bpm;
                _title = entry.Label;
                _audio?.LoadMusic(entry.AudioFile);
            }

            _engine = new JudgeEngine(_settings);
            _engine.Load(track);
            _cursor = new ScriptCursor(_script);
            _beatSec = (60f / bpm) * 4f / 4.0;   // 파서와 같은 float 마디 길이를 4등분
            UpdateMusicStart();

            _clock.Reset(startAtSec);
            _clock.Paused = startPaused;
            _clock.Speed = speed;
            _stepper = new FrameStepper(startAtSec, StepSec);
            if (_scenario != null && _scenario.HasHitch && inputMode != InputMode.Keyboard)
                _stepper.ScheduleHitch(_scenario.HitchAt, _scenario.HitchSec);

            _events.Clear();
            _inputMarks.Clear();
            _keyQueue.Clear();
            _pendingKeys.Clear();
            _prevRealtime = Time.realtimeSinceStartupAsDouble;
            _endTime = Math.Max(track.Length > 0 ? track[^1].Time : 0, _script.LastTime) + TailSec;
            _summaryLogged = false;
            _result = "";

            if (_stepper.TryTake(_clock.Now, out double first)) StepEngine(first);
            Reanchor();
            _panel?.OnRestarted();
        }

        // ---------------- 내부 ----------------

        private void LoadCharts()
        {
            _charts.Clear();
            foreach (MusicSO music in Resources.LoadAll<MusicSO>("Music").OrderBy(m => m.id))
            {
                if (music.chartFile == null) continue;
                string song = string.IsNullOrEmpty(music.audioFilePath) ? music.name : Path.GetFileNameWithoutExtension(music.audioFilePath);

                foreach (KeyValuePair<Difficulty, TextAsset> chart in music.chartFile.OrderBy(c => c.Key))
                {
                    if (chart.Value == null) continue;
                    string level = music.level != null && music.level.TryGetValue(chart.Key, out int lv) && lv > 0 ? $" Lv{lv}" : "";
                    _charts.Add(new ChartEntry
                    {
                        Label = $"{song} · {chart.Key}{level}",
                        Chart = chart.Value,
                        Bpm = music.bpm,
                        AudioFile = music.audioFilePath,
                    });
                }
            }
        }

        private void UpdateMusicStart()
        {
            // 게임은 0번(빈) 마디만큼 늦게 음악을 시작한다(GameManager.StartGame -> StartMusic(barDuration))
            _musicStartClock = _beatSec * 4 + audioOffsetMs / 1000.0;
        }

        private void Reanchor()
        {
            if (_audio == null || _engine == null) return;

            bool running = !_clock.Paused;
            double from = _audio.Anchor(_clock.Now, _clock.Speed, running, musicOn && source == SourceMode.Chart, _musicStartClock);

            _clickInput = 0;
            while (_clickInput < _script.Count && _script.Inputs[_clickInput].Time < from) _clickInput++;
            _nextBeat = (long)Math.Ceiling(from / _beatSec);
            _nextDriftCheck = Time.realtimeSinceStartupAsDouble + DriftCheckSec;
        }

        private void ScheduleClicks()
        {
            if (_clock.Paused) return;
            double horizon = _clock.Now + ClickLookaheadSec * _clock.Speed;

            IReadOnlyList<ScriptedInput> inputs = _script.Inputs;
            for (; _clickInput < inputs.Count && inputs[_clickInput].Time <= horizon; _clickInput++)
            {
                if (hitsoundOn && inputs[_clickInput].IsPress) _audio.ScheduleClick(inputs[_clickInput].Time, ClickKind.Hit);
            }

            for (; _nextBeat * _beatSec <= horizon; _nextBeat++)
            {
                if (metronomeOn && _nextBeat >= 0)
                    _audio.ScheduleClick(_nextBeat * _beatSec, _nextBeat % 4 == 0 ? ClickKind.Downbeat : ClickKind.Beat);
            }
        }

        private void CheckMusicDrift()
        {
            if (_clock.Paused || Time.realtimeSinceStartupAsDouble < _nextDriftCheck) return;
            _nextDriftCheck = Time.realtimeSinceStartupAsDouble + DriftCheckSec;
            if (Math.Abs(_audio.MusicDrift(_clock.Now, _musicStartClock)) > MaxDriftSec) Reanchor();
        }

        private void StepEngine(double frame)
        {
            _frameEvents.Clear();
            _cursor.Feed(_engine, frame, _frameEvents, _inputMarks);

            while (_pendingKeys.Count > 0 && _pendingKeys.Peek().Time <= frame)
            {
                ScriptedInput key = _pendingKeys.Dequeue();
                if (key.IsPress) _engine.Press(key.Lane, key.Time, _frameEvents);
                else _engine.Release(key.Lane, key.Time, _frameEvents);
                _inputMarks.Add(key);
            }

            _engine.Advance(frame, _frameEvents);
            _events.AddRange(_frameEvents);
        }

        private InputScript BuildChartScript(JudgeNote[] track)
        {
            switch (inputMode)
            {
                case InputMode.Keyboard:
                    return new InputScript();

                case InputMode.Script:
                    if (inputScript == null)
                    {
                        Debug.LogWarning("[JudgeSandbox] 스크립트 입력인데 inputScript가 비어 있다");
                        return new InputScript();
                    }
                    var errors = new List<string>();
                    InputScript parsed = InputScript.Parse(inputScript.text, errors);
                    foreach (string error in errors) Debug.LogWarning($"[JudgeSandbox] {error}");
                    return parsed;

                default:
                    AutoplayOptions options = AutoplayOptions.Perfect;
                    options.OffsetSec = autoplayOffsetMs / 1000.0;
                    options.BreakHoldChance = breakHoldChance;
                    options.DropTapChance = dropTapChance;
                    options.Seed = seed;
                    return Autoplay.Build(track, options);
            }
        }

        private void OnKey(Lane lane, bool isPress, double realtime)
        {
            if (inputMode != InputMode.Keyboard || _engine == null) return;
            _keyQueue.Enqueue((lane, isPress, realtime));
            if (isPress && hitsoundOn) _audio.PlayClickNow(ClickKind.Hit);
        }

        private void HandleShortcuts()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.periodKey.wasPressedThisFrame) StepFrame();
            if (kb.hKey.wasPressedThisFrame) InjectHitch();
            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.tabKey.wasPressedThisFrame) SetSource(source == SourceMode.Scenario ? SourceMode.Chart : SourceMode.Scenario);
            if (kb.nKey.wasPressedThisFrame) Next(+1);
            if (kb.bKey.wasPressedThisFrame) Next(-1);
        }

        private void Next(int delta)
        {
            if (source == SourceMode.Scenario) SelectScenario(scenarioIndex + delta);
            else SelectChart(chartIndex + delta);
        }

        // 이번 화면 프레임 동안의 realtime 구간을 시계 구간에 선형으로 대응시켜 판정 프레임에 넣을 입력으로 쌓는다.
        // 일시정지 중이면 모두 현재 시각이 된다
        private void QueueKeyboard(double prevReal, double nowReal, double prevClock, double nowClock)
        {
            while (_keyQueue.Count > 0)
            {
                (Lane lane, bool isPress, double realtime) = _keyQueue.Dequeue();
                double a = nowReal > prevReal ? Math.Clamp((realtime - prevReal) / (nowReal - prevReal), 0, 1) : 1;
                _pendingKeys.Enqueue(new ScriptedInput(prevClock + a * (nowClock - prevClock), lane, isPress));
            }
        }

        private void LogSummary()
        {
            _summaryLogged = true;
            _result = "-";

            if (_scenario != null && inputMode != InputMode.Keyboard)
            {
                var result = new ScenarioResult(JudgeOutcome.Summarize(_events), _scenario.Expected);
                _result = result.Passed ? "PASS" : "FAIL";
                if (!result.Passed) Debug.LogWarning($"[JudgeSandbox] {_scenario.Name} {result.FirstMismatch}");
            }

            Debug.Log($"[JudgeSandbox] SUMMARY {_title} {Tally()} result={_result}");
        }

        public string Tally()
        {
            int Count(JudgeType grade) => _events.Count(e => !e.IsMiss && e.Judge == grade);
            return $"notes={_engine?.Count ?? 0} judged={_events.Count} Perfect={Count(JudgeType.Perfect)} Master={Count(JudgeType.Master)} " +
                   $"Ideal={Count(JudgeType.Ideal)} Kind={Count(JudgeType.Kind)} Umm={Count(JudgeType.Umm)} Miss={_events.Count(e => e.IsMiss)}";
        }

        private static void RunAllScenariosHeadless()
        {
            int passed = 0;
            foreach (JudgeScenario scenario in JudgeScenarios.All)
            {
                ScenarioResult result = scenario.Run();
                if (result.Passed) passed++;
                else Debug.LogWarning($"[JudgeSandbox] FAIL {scenario.Name}: {result.FirstMismatch}");
            }
            Debug.Log($"[JudgeSandbox] SUMMARY scenarios {passed}/{JudgeScenarios.All.Count} PASS");
        }
    }
}
