#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Game.Timing;
using SCOdyssey.Game.Timing.Judgement;
using SCOdyssey.Game.Timing.LaneInput;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SCOdyssey.Testing.AudioHarness
{
    // 판정 타이밍 확인(S3). 게임의 입력 맵(Game.Lane1~4: Q, A, ', /)을 새 입력 소스에 Push하고, JudgementDriver와
    // GameplayTimingBinding을 거쳐 받은 입력을 기록한다.
    //   SP11: 클릭 트랙(0.5초 간격)에 맞춰 1분 탭. 프레임 시각 - 입력 시각 분포, 매퍼 오프셋 변동, 탭 오차(참고).
    //   SP-IN: 레인을 누른 채 입력 맵 끄기(Synthetic 구간) / alt-tab(포커스). 그때 오는 release가 판정되지 않아야 한다.
    internal sealed class HarnessTimingChecks : IDisposable
    {
        private const double TapSeconds = 60.0;
        private const double SettleSeconds = 0.3;
        private const double FocusTimeoutSeconds = 30.0;
        private const double LongRunTargetMinutes = 30.0;

        private readonly HarnessSongChecks _songs;
        private readonly Action<string> _status;
        private readonly InputSystem_Actions _actions = new InputSystem_Actions();
        private readonly UnityInputSystemTimestampSource _source = new UnityInputSystemTimestampSource();
        private readonly JudgementDriver _driver;
        private readonly bool[] _held = new bool[5];
        private readonly List<double> _frameLagMs = new List<double>();
        private readonly List<double> _mapperOffsets = new List<double>();
        private GameplayTimingBinding _binding;
        private bool _recordingTaps;

        // 입력 누계(배치 경계 비교용).
        private int _releases;
        private int _judgeableReleases;
        private int _judgeablePresses;
        private string _lastExternalPause = "";

        // 포커스를 잃기 직전 프레임의 상태(SP-IN alt-tab).
        private int _heldBeforeFocusLoss;
        private int _releasesBeforeFocusLoss;
        private int _judgeableReleasesBeforeFocusLoss;

        // 매퍼 장기 변동(하네스 시작부터).
        private readonly long _longRunStartQpc = Qpc.Now;
        private double _longRunMin = double.MaxValue;
        private double _longRunMax = double.MinValue;

        public HarnessTimingChecks(HarnessSongChecks songs, Action<string> status, GameObject host)
        {
            _songs = songs;
            _status = status;
            _driver = JudgementDriver.Install(host, _source, ReadJudgmentOffsetSteps);

            BindLane(_actions.Game.Lane1, 1);
            BindLane(_actions.Game.Lane2, 2);
            BindLane(_actions.Game.Lane3, 3);
            BindLane(_actions.Game.Lane4, 4);
            _actions.Game.Enable();
        }

        public int JudgmentOffsetSteps { get; set; }

        public string LastExternalPause
        {
            get { return _lastExternalPause; }
        }

        public string Describe()
        {
            string latch = "래치 전";
            if (_driver.IsJudgmentOffsetLatched) latch = _driver.LatchedJudgmentOffsetSteps + "단계";
            return string.Format("입력 맵 켜짐 {0}, 누른 레인 {1}, 판정 입력 {2}, release {3}(판정됨 {4}), 판정 싱크 {5}, 붙음 {6}",
                _actions.Game.enabled, HeldCount(), _judgeablePresses, _releases, _judgeableReleases, latch, _driver.IsAttached);
        }

        public void Dispose()
        {
            DetachBinding();
            _actions.Game.Disable();
            _actions.Dispose();
        }

        public void DetachBinding()
        {
            if (_binding != null) _binding.Dispose();
            _binding = null;
        }

        // 매 프레임(JudgementDriver 다음) 부른다.
        public void Tick()
        {
            double offset = _source.MapperOffsetSeconds;
            if (offset != 0)
            {
                if (offset < _longRunMin) _longRunMin = offset;
                if (offset > _longRunMax) _longRunMax = offset;
            }
            if (_recordingTaps) _mapperOffsets.Add(offset);

            if (Application.isFocused)
            {
                _heldBeforeFocusLoss = HeldCount();
                _releasesBeforeFocusLoss = _releases;
                _judgeableReleasesBeforeFocusLoss = _judgeableReleases;
            }
        }

        // SP11: 60초 동안 클릭에 맞춰 레인 키를 누른다.
        public IEnumerator RunTapTest()
        {
            yield return PrepareSession();
            if (_binding == null) yield break;

            _frameLagMs.Clear();
            _mapperOffsets.Clear();
            _driver.TimingLog.Clear();
            int clampedBefore = _driver.ClampedInputs;
            int rejectedBefore = _driver.RejectedInputs;
            int droppedBefore = _source.DroppedCount;
            _recordingTaps = true;

            double elapsed = 0;
            while (elapsed < TapSeconds)
            {
                _status(string.Format("SP11 탭 테스트: 클릭에 맞춰 레인 키(Q, A, ', /)를 누르세요. 남은 시간 {0:F0}초, 탭 {1}회", TapSeconds - elapsed, _frameLagMs.Count));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _recordingTaps = false;
            _songs.Stop();

            string context = FpsContext();
            if (_frameLagMs.Count < 10)
            {
                HarnessReport.Summary("SP11", HarnessReport.Result.Fail, "탭 10회 이상", "탭 " + _frameLagMs.Count + "회", context);
                _status("SP11: 탭이 부족합니다.");
                yield break;
            }

            _frameLagMs.Sort();
            _mapperOffsets.Sort();
            double mapperRange = (Percentile(_mapperOffsets, 0.99) - Percentile(_mapperOffsets, 0.01)) * 1000.0;
            TimingStats taps = _driver.TimingLog.Summarize(TimingKind.Press, TimingLog.Capacity);
            bool pass = mapperRange < 0.1;
            string measured = string.Format("탭 {0}회, 프레임 시각 - 입력 시각 평균 {1}ms p50 {2}ms p95 {3}ms 최대 {4}ms, 매퍼 오프셋 변동(p1~p99) {5}ms, " +
                                            "프레임 시각으로 자름 {6}, 거부 {7}, 버림 {8}, 탭 오차(참고) 평균 {9}ms 표준편차 {10}ms",
                _frameLagMs.Count, Ms(Mean(_frameLagMs)), Ms(Percentile(_frameLagMs, 0.5)), Ms(Percentile(_frameLagMs, 0.95)), Ms(_frameLagMs[_frameLagMs.Count - 1]),
                HarnessReport.Num(mapperRange, "0.000"), _driver.ClampedInputs - clampedBefore, _driver.RejectedInputs - rejectedBefore,
                _source.DroppedCount - droppedBefore, HarnessReport.Num(taps.MeanMs, "+0.0;-0.0;0.0"), HarnessReport.Num(taps.StdDevMs, "0.0"));
            HarnessReport.Summary("SP11", HarnessReport.PassIf(pass), "매퍼 변동 < 0.1ms(분포는 기록용)", measured, context);
            _status("SP11 완료");
        }

        // SP11 장기: 하네스를 켠 뒤 지금까지 매퍼 오프셋(중앙값)이 움직인 폭. 30분 이상 켜 둔 뒤 누른다.
        public void ReportLongRun()
        {
            double minutes = Qpc.ToSeconds(Qpc.Now - _longRunStartQpc) / 60.0;
            if (_longRunMax < _longRunMin)
            {
                HarnessReport.Summary("SP11-mapper", HarnessReport.Result.Info, "매퍼 표본 있음", "표본 없음", FpsContext());
                return;
            }
            double rangeMs = (_longRunMax - _longRunMin) * 1000.0;
            HarnessReport.Result result = HarnessReport.PassIf(rangeMs < 0.1);
            if (minutes < LongRunTargetMinutes) result = HarnessReport.Result.Info;
            HarnessReport.Summary("SP11-mapper", result, "30분 이상, 매퍼 변동 < 0.1ms",
                string.Format("경과 {0}분, 변동(최대 - 최소) {1}ms", HarnessReport.Num(minutes, "0.0"), HarnessReport.Num(rangeMs, "0.000")), FpsContext());
            _status("SP11 매퍼 장기 변동을 기록했습니다.");
        }

        // SP-IN(a): 레인을 누른 채 입력 맵을 끈다. Disable 안에서 동기로 오는 cancel이 Synthetic(판정 불가)이어야 한다.
        public IEnumerator RunMapDisable()
        {
            yield return PrepareSession();
            if (_binding == null) yield break;

            yield return Countdown(3.0, "SP-IN 입력 맵 끄기: 레인 키 하나 이상을 누른 채로 두세요. {0:F1}초 뒤 입력 맵을 끕니다.");
            int held = HeldCount();
            int releasesBefore = _releases;
            int judgeableBefore = _judgeableReleases;
            int syntheticBefore = _source.SyntheticCount;

            _source.BeginSynthetic();
            try
            {
                _actions.Game.Disable();
            }
            finally
            {
                _source.EndSynthetic();
            }
            int syntheticInScope = _source.SyntheticCount - syntheticBefore;

            yield return Wait(SettleSeconds);
            int releases = _releases - releasesBefore;
            int judgeable = _judgeableReleases - judgeableBefore;
            ClearHeld();
            _actions.Game.Enable();
            _songs.Stop();

            bool pass = held > 0 && releases == held && judgeable == 0;
            string measured = string.Format("누른 레인 {0}, 끈 뒤 release {1}(구간 안 Synthetic {2}, 판정됨 {3}), 세션 {4}",
                held, releases, syntheticInScope, judgeable, SessionState());
            if (held == 0) measured += " (누른 레인이 없어 확인 불가)";
            HarnessReport.Summary("SP-IN-map", HarnessReport.PassIf(pass), "release 수 = 누른 레인 수, 판정된 release 0", measured, FpsContext());
            _status("SP-IN 입력 맵 끄기 완료. 키를 놓으세요.");
        }

        // SP-IN(b): 레인을 누른 채 alt-tab. 포커스 상실 때 Input System이 만드는 cancel이 판정되지 않아야 한다.
        public IEnumerator RunFocusLoss()
        {
            yield return PrepareSession();
            if (_binding == null) yield break;

            double elapsed = 0;
            while (Application.isFocused && elapsed < FocusTimeoutSeconds)
            {
                _status(string.Format("SP-IN alt-tab: 레인 키를 누른 채 alt-tab으로 다른 창에 2초 이상 있다가 돌아오세요. ({0:F0}초 남음)", FocusTimeoutSeconds - elapsed));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (Application.isFocused)
            {
                _songs.Stop();
                _status("SP-IN alt-tab: 시간 안에 포커스를 잃지 않아 취소했습니다.");
                yield break;
            }

            int held = _heldBeforeFocusLoss;
            int releasesBefore = _releasesBeforeFocusLoss;
            int judgeableBefore = _judgeableReleasesBeforeFocusLoss;
            string stateWhileAway = "";
            while (!Application.isFocused)
            {
                stateWhileAway = SessionState();
                yield return null;
            }
            yield return Wait(SettleSeconds);

            int releases = _releases - releasesBefore;
            int judgeable = _judgeableReleases - judgeableBefore;
            string stateAfter = SessionState();
            ClearHeld();
            _songs.Stop();

            bool pass = held > 0 && releases > 0 && judgeable == 0;
            string measured = string.Format("누른 레인 {0}, 포커스 상실 뒤 release {1}(판정됨 {2}), 떠나 있는 동안 세션 {3}, 돌아온 뒤 {4}, 외부 일시정지 알림 '{5}', runInBackground {6}",
                held, releases, judgeable, stateWhileAway, stateAfter, _lastExternalPause, Application.runInBackground);
            if (held == 0) measured += " (누른 레인이 없어 확인 불가)";
            HarnessReport.Summary("SP-IN-focus", HarnessReport.PassIf(pass), "release 1개 이상, 판정된 release 0", measured, FpsContext());
            _status("SP-IN alt-tab 완료");
        }

        private IEnumerator PrepareSession()
        {
            DetachBinding();
            _lastExternalPause = "";
            yield return _songs.Load();
            ISongSession session = _songs.Session;
            if (session == null)
            {
                _status("곡 로드 실패: " + _songs.LastLoad);
                yield break;
            }

            _binding = GameplayTimingBinding.Attach(_driver, session, OnAdvance, OnLaneInput, IsRunning, OnExternalPause);
            _songs.Start();
            double waited = 0;
            while (session.State != SongSessionState.LeadIn && session.State != SongSessionState.Playing && waited < 3.0)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void BindLane(InputAction action, int lane)
        {
            action.performed += ctx => _source.Push(lane, true, ctx.time);
            action.canceled += ctx => _source.Push(lane, false, ctx.time);
        }

        private int ReadJudgmentOffsetSteps()
        {
            return JudgmentOffsetSteps;
        }

        private static bool IsRunning()
        {
            return true;
        }

        private void OnAdvance(double songTime, double judgeTime)
        {
        }

        private void OnLaneInput(in JudgedInput input)
        {
            if (input.Lane < 1 || input.Lane >= _held.Length) return;
            if (!input.IsDown)
            {
                _held[input.Lane] = false;
                _releases++;
                if (input.Judgeable) _judgeableReleases++;
                return;
            }

            _held[input.Lane] = true;
            _judgeablePresses++;
            if (!_recordingTaps || _binding == null) return;

            SongFrame frame = _binding.Session.Clock.Frame;
            _frameLagMs.Add(Qpc.ToSeconds(frame.QpcTicks - input.QpcTicks) * 1000.0);

            // 클릭은 곡 시각 리드인 + 0.5초 × k에 있다(노트 싱크 0).
            double beat = HarnessSongChecks.LeadInSeconds + Math.Round((input.JudgeTime - HarnessSongChecks.LeadInSeconds) / HarnessSongChecks.ClickInterval) * HarnessSongChecks.ClickInterval;
            _driver.TimingLog.Record(TimingKind.Press, 0, (input.JudgeTime - beat) * 1000.0);
        }

        private void OnExternalPause(PauseReason reason)
        {
            _lastExternalPause = reason.ToString();
        }

        private int HeldCount()
        {
            int count = 0;
            for (int i = 1; i < _held.Length; i++)
            {
                if (_held[i]) count++;
            }
            return count;
        }

        private void ClearHeld()
        {
            for (int i = 0; i < _held.Length; i++) _held[i] = false;
        }

        private string SessionState()
        {
            ISongSession session = _songs.Session;
            if (session == null) return "없음";
            return session.State + "(" + session.PauseReason + ")";
        }

        private static string FpsContext()
        {
            string target = "무제한";
            if (Application.targetFrameRate > 0) target = Application.targetFrameRate.ToString();
            return "목표 fps " + target + ", 실제 fps " + HarnessReport.Num(1.0 / Time.smoothDeltaTime, "0") + ", 포커스 " + Application.isFocused;
        }

        private IEnumerator Countdown(double seconds, string format)
        {
            double elapsed = 0;
            while (elapsed < seconds)
            {
                _status(string.Format(format, seconds - elapsed));
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static IEnumerator Wait(double seconds)
        {
            double elapsed = 0;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static double Mean(List<double> values)
        {
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        private static double Percentile(List<double> sorted, double fraction)
        {
            int index = (int)Math.Round((sorted.Count - 1) * fraction);
            return sorted[index];
        }

        private static string Ms(double value)
        {
            return HarnessReport.Num(value, "0.00");
        }
    }
}
#endif
