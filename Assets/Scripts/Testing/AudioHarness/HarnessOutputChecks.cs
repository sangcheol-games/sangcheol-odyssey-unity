#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio;
using SCOdyssey.Audio.Hosting;
using UnityEngine;

namespace SCOdyssey.Testing.AudioHarness
{
    // 출력 관리 확인(S2b): 장치 목록(SP9), 설정 적용과 재구성 반복(SP6), ASIO 전환(SP8), 장치 사건 기록(SP10).
    internal sealed class HarnessOutputChecks
    {
        private const int Repeats = 50;
        private static readonly int[] AlternatingBuffers = { 256, 512 };

        private readonly Func<AudioModule> _module;
        private readonly Action<string> _status;
        private IReadOnlyList<AudioDeviceInfo> _devices = new AudioDeviceInfo[0];
        private int _deviceIndex = -1;      // -1 = 기본 장치 따라가기
        private int _lastGeneration;
        private EngineStatus _lastStatus;
        private string _lastDevice = "";

        public HarnessOutputChecks(Func<AudioModule> module, Action<string> status)
        {
            _module = module;
            _status = status;
        }

        public AudioOutputKind Kind { get; set; } = AudioOutputKind.Wasapi;
        public int BufferLength { get; set; } = 256;
        public int BufferCount { get; set; } = 4;
        public string LastApply { get; private set; } = "";

        public string DeviceLabel
        {
            get
            {
                if (_deviceIndex < 0 || _deviceIndex >= _devices.Count) return "기본 장치 따라가기 (목록 " + _devices.Count + "개)";
                return "[" + _deviceIndex + "] " + _devices[_deviceIndex].Name + " (" + _devices[_deviceIndex].SystemRate + "Hz)";
            }
        }

        public void ChangeDevice(int delta)
        {
            int count = _devices.Count + 1;           // 0번 = 기본 장치 따라가기
            int position = (_deviceIndex + 1 + delta + count) % count;
            _deviceIndex = position - 1;
        }

        public IEnumerator RefreshDevices()
        {
            IReadOnlyList<AudioDeviceInfo> devices = _devices;
            yield return _module().Output.GetDevicesAsync(Kind, true, CancellationToken.None).ToCoroutine(r => devices = r);
            _devices = devices;
            if (_deviceIndex >= _devices.Count) _deviceIndex = -1;
        }

        public AudioOutputRequest CurrentRequest()
        {
            Guid id = Guid.Empty;
            string name = "";
            if (_deviceIndex >= 0 && _deviceIndex < _devices.Count)
            {
                id = _devices[_deviceIndex].Id;
                name = _devices[_deviceIndex].Name;
            }
            return new AudioOutputRequest(Kind, id, name, BufferLength, BufferCount);
        }

        public IEnumerator Apply()
        {
            AudioApplyResult result = default;
            var watch = Stopwatch.StartNew();
            yield return _module().Output.ApplyAsync(CurrentRequest(), CancellationToken.None).ToCoroutine(r => result = r);
            LastApply = result.Outcome + " (" + HarnessReport.Num(watch.Elapsed.TotalMilliseconds, "0") + "ms): " + result.Message;
            HarnessReport.Summary("apply", HarnessReport.Result.Info, "요청 " + Describe(CurrentRequest()), LastApply, Describe());
        }

        // SP6: 로비 BGM을 튼 채로 설정 적용 50회(버퍼 256↔512)와 같은 구성 close→init 50회.
        public IEnumerator RunSp6(OneShotId click)
        {
            AudioModule module = _module();
            yield return module.Music.Lobby.PlayAsync(HarnessSongChecks.TrackFile, true, CancellationToken.None).ToCoroutine();

            int applyFailures = 0;
            int restoreFailures = 0;
            double maxApply = 0;
            string lastError = "";
            for (int i = 0; i < Repeats; i++)
            {
                _status(string.Format("SP6 설정 적용 {0}/{1}", i + 1, Repeats));
                var request = new AudioOutputRequest(Kind, CurrentRequest().DeviceId, CurrentRequest().DeviceName, AlternatingBuffers[i % 2], BufferCount);
                AudioApplyResult result = default;
                var watch = Stopwatch.StartNew();
                yield return module.Output.ApplyAsync(request, CancellationToken.None).ToCoroutine(r => result = r);
                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms > maxApply) maxApply = ms;
                if (result.Outcome != AudioApplyOutcome.Applied)
                {
                    applyFailures++;
                    lastError = result.Outcome + ": " + result.Message;
                }
                bool restored = false;
                yield return WaitFor(() => module.Music.Lobby.IsPlaying, 1.0, ok => restored = ok);
                if (!restored) restoreFailures++;
                module.OneShots.Play(click);
            }

            int reinitFailures = 0;
            double maxReinit = 0;
            for (int i = 0; i < Repeats; i++)
            {
                _status(string.Format("SP6 close→init {0}/{1}", i + 1, Repeats));
                var watch = Stopwatch.StartNew();
                module.Reinitialize();
                double ms = watch.Elapsed.TotalMilliseconds;
                if (ms > maxReinit) maxReinit = ms;
                if (module.Engine.Status != EngineStatus.Running) reinitFailures++;
                bool restored = false;
                yield return WaitFor(() => module.Music.Lobby.IsPlaying, 1.0, ok => restored = ok);
                if (!restored) restoreFailures++;
                module.OneShots.Play(click);
            }
            module.Music.Lobby.Stop();

            bool pass = applyFailures == 0 && reinitFailures == 0 && restoreFailures == 0 && maxApply < 1000 && maxReinit < 1000;
            string measured = string.Format("적용 실패 {0}/{1}(최대 {2}ms), close→init 실패 {3}/{1}(최대 {4}ms), 음악 복원 실패 {5}, 원샷 id 유효: {6}",
                applyFailures, Repeats, HarnessReport.Num(maxApply, "0"), reinitFailures, HarnessReport.Num(maxReinit, "0"), restoreFailures, click.IsValid);
            if (applyFailures > 0) measured += ", 마지막 오류: " + lastError;
            HarnessReport.Summary("SP6", HarnessReport.PassIf(pass), "실패 0, 1회 < 1초, 음악·원샷 복원", measured, Describe());
            _status("SP6 완료");
        }

        // SP9: 로비 BGM을 튼 채로 타입별 장치 목록을 새로 읽는다(메인 출력 끊김은 루프백 녹음으로 확인).
        public IEnumerator RunSp9()
        {
            AudioModule module = _module();
            yield return module.Music.Lobby.PlayAsync(HarnessSongChecks.TrackFile, true, CancellationToken.None).ToCoroutine();
            var parts = new List<string>();
            AudioOutputKind[] kinds = { AudioOutputKind.Wasapi, AudioOutputKind.Asio };
            for (int round = 0; round < 3; round++)
            {
                for (int k = 0; k < kinds.Length; k++)
                {
                    IReadOnlyList<AudioDeviceInfo> list = null;
                    yield return module.Output.GetDevicesAsync(kinds[k], true, CancellationToken.None).ToCoroutine(r => list = r);
                    parts.Add(kinds[k] + " " + list.Count + "개 " + HarnessReport.Num(module.LastDeviceEnumerateMilliseconds, "0.0") + "ms");
                    yield return Wait(0.5);
                }
            }
            bool stillPlaying = module.Music.Lobby.IsPlaying;
            module.Music.Lobby.Stop();
            HarnessReport.Summary("SP9", HarnessReport.PassIf(stillPlaying && module.Engine.Status == EngineStatus.Running),
                "열거 중에도 메인 출력 유지(끊김은 녹음으로 확인)", string.Join(", ", parts) + ", 재생 유지: " + stillPlaying, Describe());
            _status("SP9 완료");
        }

        // SP10: 세대·상태·장치가 바뀌면 한 줄씩 남긴다(USB 분리, 기본 장치 변경 등 수동 확인용). 매 프레임 부른다.
        public void Watch(ISongSession session, bool suppressed)
        {
            AudioModule module = _module();
            if (module == null || module.IsShutDown) return;
            IAudioEngine engine = module.Engine;
            string device = engine.CurrentOutput.DeviceName;
            if (engine.Generation == _lastGeneration && engine.Status == _lastStatus && device == _lastDevice) return;

            bool first = _lastGeneration == 0;
            _lastGeneration = engine.Generation;
            _lastStatus = engine.Status;
            _lastDevice = device;
            if (first || suppressed) return;

            string songState = "곡 없음";
            if (session != null) songState = "곡 " + session.State + "(사유 " + session.PauseReason + ")";
            HarnessReport.Summary("SP10-event", HarnessReport.Result.Info, "장치 사건 뒤 상태", "상태 " + engine.Status + ", " + songState, Describe());
        }

        private string Describe()
        {
            AudioModule module = _module();
            if (module == null || module.IsShutDown) return "모듈 없음";
            AudioOutputInfo o = module.Engine.CurrentOutput;
            return string.Format("출력: {0}, 장치: {1}, 레이트: {2}, 버퍼: {3}x{4}, 세대: {5}",
                o.Kind, o.DeviceName, o.SampleRate, o.BufferLength, o.BufferCount, module.Engine.Generation);
        }

        private static string Describe(AudioOutputRequest request)
        {
            string device = "기본 장치";
            if (request.DeviceId != Guid.Empty) device = request.DeviceName;
            return request.Kind + " / " + device + " / " + request.BufferLength + "x" + request.BufferCount;
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

        private static IEnumerator WaitFor(Func<bool> condition, double timeoutSeconds, Action<bool> done)
        {
            double elapsed = 0;
            while (!condition() && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            done(condition());
        }
    }
}
#endif
