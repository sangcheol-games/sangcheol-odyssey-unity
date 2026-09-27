#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio;
using SCOdyssey.Audio.Diagnostics;
using SCOdyssey.Audio.Hosting;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Testing.AudioHarness
{
    // 곡 재생 확인(S2a). 곡은 0.5초마다 왼쪽 채널에 클릭이 있는 60초 트랙이고, 진단 메트로놈은 같은 박을 오른쪽에 낸다.
    // 루프백 녹음에서 두 클릭이 겹치는지 보면 앵커 커밋과 일시정지·재개 예약이 맞는지 알 수 있다(SP4).
    internal sealed class HarnessSongChecks
    {
        public const string TrackFile = "harness_clicktrack.wav";
        public const double LeadInSeconds = 1.0;
        public const double TrackSeconds = 60.0;
        public const double ClickInterval = 0.5;
        private const int PauseCycles = 20;

        private readonly Func<AudioModule> _module;
        private readonly Action<string> _status;
        private readonly Action<SongSessionEvent> _onChanged;
        private readonly List<string> _events = new List<string>();
        private ISongSession _session;
        private long _resumeCalledQpc;
        private double _lastResumeLatency;
        private bool _recovered;

        public HarnessSongChecks(Func<AudioModule> module, Action<string> status)
        {
            _module = module;
            _status = status;
            _onChanged = OnSessionChanged;
        }

        public ISongSession Session
        {
            get { return _session; }
        }

        public string LastLoad { get; private set; } = "";

        public string RecentEvents
        {
            get
            {
                int from = _events.Count - 6;
                if (from < 0) from = 0;
                return string.Join(" → ", _events.GetRange(from, _events.Count - from));
            }
        }

        public static void PrepareFiles(string musicFolder)
        {
            Directory.CreateDirectory(musicFolder);
            string path = Path.Combine(musicFolder, TrackFile);
            if (!File.Exists(path)) HarnessWav.WriteClickTrack(path, 48000, TrackSeconds, ClickInterval);
        }

        public IEnumerator Load()
        {
            Detach();
            SongLoadResult result = default;
            yield return _module().Songs.LoadAsync(TrackFile, CancellationToken.None).ToCoroutine(r => result = r);
            LastLoad = result.Status + " " + result.Detail;
            if (!result.Ok) yield break;
            _session = result.Session;
            _session.Changed += _onChanged;
            _events.Clear();
        }

        public void Start()
        {
            if (_session == null) return;
            ConfigureMetronome(true);
            _session.Start(LeadInSeconds, 0);
        }

        public void Pause()
        {
            if (_session != null) _session.Pause(PauseReason.User);
        }

        public void Resume()
        {
            if (_session == null) return;
            _resumeCalledQpc = Qpc.Now;
            _session.Resume();
        }

        public void Stop()
        {
            if (_session != null) _session.Stop();
            ConfigureMetronome(false);
        }

        public void Detach()
        {
            if (_session != null) _session.Changed -= _onChanged;
            _session = null;
        }

        // SP4: 리드인 중 1회 + 곡 도중 20회 일시정지·재개. 재개 지연과 메트로놈 늦은 예약을 기록하고, 정확도는 루프백 녹음으로 잰다.
        public IEnumerator RunSp4()
        {
            _module().Metronome.ResetCounters();
            yield return Load();
            if (_session == null)
            {
                HarnessReport.Summary("SP4-module", HarnessReport.Result.Fail, "곡 로드", LastLoad, "");
                yield break;
            }

            Start();
            yield return Wait(0.5);
            double maxLatency = 0;
            double sumLatency = 0;
            int resumes = 0;
            int unexpected = 0;
            for (int cycle = 0; cycle <= PauseCycles; cycle++)
            {
                _status(string.Format("SP4 일시정지·재개 {0}/{1}", cycle, PauseCycles));
                Pause();
                if (_session.State != SongSessionState.Paused) unexpected++;
                yield return Wait(1.0);
                Resume();
                yield return WaitFor(() => _session.State == SongSessionState.LeadIn || _session.State == SongSessionState.Playing, 1.0);
                if (_session.State != SongSessionState.LeadIn && _session.State != SongSessionState.Playing) unexpected++;
                resumes++;
                sumLatency += _lastResumeLatency;
                if (_lastResumeLatency > maxLatency) maxLatency = _lastResumeLatency;
                yield return Wait(2.5);
            }
            yield return Wait(5.0);

            SongMetronome metronome = _module().Metronome;
            bool pass = unexpected == 0 && maxLatency < 1.0 && metronome.LateClicks == 0;
            string measured = string.Format("일시정지·재개 {0}회, 재개 지연 평균 {1}ms 최대 {2}ms, 상태 이상 {3}회, 메트로놈 예약 {4}회 늦은 예약 {5}회, 음원 종료 {6}, (루프백) 곡–메트로놈 시작 차는 녹음으로 분석",
                resumes, HarnessReport.Num(sumLatency / resumes * 1000.0, "0.0"), HarnessReport.Num(maxLatency * 1000.0, "0.0"),
                unexpected, metronome.ScheduledClicks, metronome.LateClicks, _session.IsAudioFinished);
            Stop();
            HarnessReport.Summary("SP4-module", HarnessReport.PassIf(pass), "재개 지연 < 1초, 늦은 예약 0, 상태 이상 0", measured, Describe());
            _status("SP4 완료");
        }

        // 재구성 중 세션 처리: Ready, Starting, Playing, Paused에서 강제 재구성을 하고 각각 제대로 이어지는지 본다.
        public IEnumerator RunReconfigure()
        {
            var results = new List<string>();
            bool pass = true;

            // 1. Ready
            yield return Load();
            if (_session == null)
            {
                HarnessReport.Summary("SP-reconf", HarnessReport.Result.Fail, "곡 로드", LastLoad, "");
                yield break;
            }
            _module().Reinitialize();
            Start();
            yield return WaitFor(IsRunning, 3.5);
            pass &= Record(results, "Ready→시작", IsRunning());
            Stop();

            // 2. Starting: 스트림이 열려 있으면 Start가 곧바로 커밋하므로, 재구성으로 스트림을 닫은 직후에 Start해
            //    Starting에 머물게 한 뒤 한 번 더 재구성한다. 다시 열리면 스스로 커밋해 진행해야 한다.
            yield return Load();
            _module().Reinitialize();
            Start();
            bool starting = _session.State == SongSessionState.Starting;
            if (starting) _module().Reinitialize();
            yield return WaitFor(IsRunning, 4.0);
            if (starting) pass &= Record(results, "Starting→진행", IsRunning());
            else results.Add("Starting 재현 안 됨(스트림이 즉시 열림, 상태 " + _session.State + ")");
            if (!IsRunning())
            {
                Resume();
                yield return WaitFor(IsRunning, 1.0);
            }
            yield return Wait(2.0);

            // 3. Playing: 장치 사유로 일시정지 → Recovered → 재개
            _recovered = false;
            _module().Reinitialize();
            bool pausedByDevice = _session.PauseReason == PauseReason.DeviceChanged;
            yield return WaitFor(() => _recovered, 3.0);
            bool recovered = _recovered && _session.State == SongSessionState.Paused;
            Resume();
            yield return WaitFor(IsRunning, 1.0);
            pass &= Record(results, "Playing→장치 일시정지", pausedByDevice);
            pass &= Record(results, "Playing→Recovered", recovered);
            pass &= Record(results, "Playing→재개", IsRunning());
            yield return Wait(2.0);

            // 4. Paused(사용자) → Recovering → Recovered → 재개
            Pause();
            _recovered = false;
            _module().Reinitialize();
            bool recovering = _session.State == SongSessionState.Recovering;
            yield return WaitFor(() => _recovered, 3.0);
            Resume();
            yield return WaitFor(IsRunning, 1.0);
            pass &= Record(results, "Paused→Recovering", recovering);
            pass &= Record(results, "Paused→Recovered→재개", _recovered && IsRunning());
            yield return Wait(2.0);
            Stop();

            HarnessReport.Summary("SP-reconf", HarnessReport.PassIf(pass), "상태별 재구성 뒤 정상 진행", string.Join(", ", results), Describe());
            _status("재구성 확인 완료");
        }

        // 프리뷰를 한 프레임에 5번 요청하면 마지막 것만 재생되고 앞의 4개는 Superseded여야 한다.
        public IEnumerator RunPreviewSupersede()
        {
            IMusicPlayer preview = _module().Music.Preview;
            var statuses = new AudioLoadStatus[5];
            int done = 0;
            for (int i = 0; i < statuses.Length; i++)
            {
                int index = i;
                preview.PlayAsync(TrackFile, true, CancellationToken.None).ContinueWith(r =>
                {
                    statuses[index] = r.Status;
                    done++;
                }).Forget();
            }
            float waited = 0;
            while (done < statuses.Length && waited < 3f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            int superseded = 0;
            for (int i = 0; i < statuses.Length - 1; i++)
            {
                if (statuses[i] == AudioLoadStatus.Superseded) superseded++;
            }
            bool lastOk = statuses[statuses.Length - 1] == AudioLoadStatus.Ok;
            bool playing = preview.IsPlaying;
            yield return Wait(1.0);
            preview.Stop();

            bool pass = superseded == statuses.Length - 1 && lastOk && playing;
            HarnessReport.Summary("music-preview", HarnessReport.PassIf(pass), "앞 4개 Superseded, 마지막 Ok·재생",
                "결과: " + string.Join("/", statuses) + ", 재생 중: " + playing, Describe());
            _status("프리뷰 확인 완료");
        }

        private void ConfigureMetronome(bool enabled)
        {
            SongMetronome metronome = _module().Metronome;
            metronome.Enabled = enabled;
            metronome.IntervalSeconds = ClickInterval;
            metronome.FirstBeatSongTime = LeadInSeconds;
            metronome.LastBeatSongTime = LeadInSeconds + TrackSeconds - 1.0;
            metronome.Pan = 1f;
        }

        private bool IsRunning()
        {
            if (_session == null) return false;
            return _session.State == SongSessionState.LeadIn || _session.State == SongSessionState.Playing;
        }

        private static bool Record(List<string> results, string label, bool ok)
        {
            string mark = "OK";
            if (!ok) mark = "실패";
            results.Add(label + " " + mark);
            return ok;
        }

        private void OnSessionChanged(SongSessionEvent sessionEvent)
        {
            _events.Add(sessionEvent.ToString());
            if (sessionEvent == SongSessionEvent.Resumed) _lastResumeLatency = Qpc.ToSeconds(Qpc.Now - _resumeCalledQpc);
            if (sessionEvent == SongSessionEvent.Recovered) _recovered = true;
        }

        private string Describe()
        {
            AudioModule module = _module();
            if (module == null || module.IsShutDown) return "모듈 없음";
            AudioOutputInfo o = module.Engine.CurrentOutput;
            return string.Format("출력: {0}, 장치: {1}, 레이트: {2}, 버퍼: {3}x{4}, 세대: {5}",
                o.Kind, o.DeviceName, o.SampleRate, o.BufferLength, o.BufferCount, module.Engine.Generation);
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

        private static IEnumerator WaitFor(Func<bool> condition, double timeoutSeconds)
        {
            double elapsed = 0;
            while (!condition() && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
#endif
