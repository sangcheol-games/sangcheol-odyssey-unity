using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace SCOdyssey.Audio.Engine
{
    // FMOD Core System의 유일한 소유자. 부팅 시도와 폴백, 상태, 세대, 프레임 update, 콜백 처리, 종료를 맡는다.
    // Degraded(NOSOUND)와 Failed에서는 IsUsable이 false이고, 재생기·원샷·세션은 FMOD를 부르지 않는다.
    internal sealed class AudioEngine : IAudioEngine
    {
        private const int MaxLoggedErrorsPerFrame = 8;

        private readonly CallbackSnapshot _callbacks = new CallbackSnapshot();
        private FMOD.System _system;
        private FMOD.ChannelGroup _systemMaster;
        private EngineStatus _status = EngineStatus.Uninitialized;
        private int _generation;
        private AudioOutputInfo _currentOutput;
        private BootAttempt _currentAttempt;
        private BootAttempt _requestedAttempt;
        private bool _hasRequested;

        public event Action<EngineStatus> StatusChanged;

        public EngineStatus Status
        {
            get { return _status; }
        }

        public int Generation
        {
            get { return _generation; }
        }

        public AudioOutputInfo CurrentOutput
        {
            get { return _currentOutput; }
        }

        public bool IsRequestedConfig
        {
            get { return _hasRequested && _status == EngineStatus.Running && _currentAttempt.SameConfig(_requestedAttempt); }
        }

        // 사용자가 요청한 구성(부팅 설정, 설정 화면 적용). 폴백으로 동작하는지 판단하는 기준이다.
        public void SetRequested(AudioOutputRequest request)
        {
            _requestedAttempt = BootPlan.FromRequest(request, "요청 구성");
            _hasRequested = true;
        }

        public bool IsUsable
        {
            get { return _status == EngineStatus.Running; }
        }

        public FMOD.System CoreSystem
        {
            get { return _system; }
        }

        // 곡 시계 기준 클록. pause·mute·pitch·volume을 절대 바꾸지 않는다.
        public FMOD.ChannelGroup SystemMaster
        {
            get { return _systemMaster; }
        }

        public string BootSummary { get; private set; } = "";
        public double InitMilliseconds { get; private set; }
        public int TotalErrors { get; private set; }

        public void Boot(AudioOutputRequest requested, bool safeMode, bool asioAllowed)
        {
            AudioThread.AssertMain("AudioEngine.Boot");
            if (_status != EngineStatus.Uninitialized) throw new InvalidOperationException("이미 부팅한 엔진입니다: " + _status);

            SetStatus(EngineStatus.Starting);
            SetRequested(requested);
            FmodDebugBridge.Install();

            List<BootAttempt> attempts = BootPlan.Build(requested, safeMode, asioAllowed);
            var failures = new List<string>();
            if (safeMode) failures.Add("안전 모드: 요청 구성 건너뜀");
            else if (requested.Kind == AudioOutputKind.Asio && !asioAllowed) failures.Add("ASIO 허용 안 됨: 요청 구성 건너뜀");

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < attempts.Count; i++)
            {
                BootAttempt attempt = attempts[i];
                FMOD.RESULT result = FMOD.Factory.System_Create(out FMOD.System system);
                if (result != FMOD.RESULT.OK)
                {
                    failures.Add(attempt.Label + ": System_Create " + result);
                    break;
                }

                string error = EngineConfigurator.TryInit(system, attempt, out AudioOutputInfo actual);
                if (error == null)
                {
                    error = AdoptSystem(system);
                }
                if (error == null)
                {
                    InitMilliseconds = watch.Elapsed.TotalMilliseconds;
                    Adopted(attempt, actual, failures, "엔진 부팅");
                    return;
                }

                failures.Add(attempt.Label + ": " + error);
                SystemCallbackHub.Uninstall(system);
                system.release();
                _system = default;
                _systemMaster = default;
            }

            InitMilliseconds = watch.Elapsed.TotalMilliseconds;
            BootSummary = "실패 | " + string.Join(" | ", failures);
            Debug.LogError("[Audio] 엔진 부팅 실패: " + BootSummary);
            SetStatus(EngineStatus.Failed);
        }

        public BootAttempt CurrentAttempt
        {
            get { return _currentAttempt; }
        }

        // 같은 System을 close → init 한다(재구성). attempts를 순서대로 시도하고, 성공한 시도의 번호를 돌려준다(모두 실패하면 -1).
        // 호출 전에 채널·Sound·ChannelGroup을 모두 해제해야 한다. 성공하면 세대가 오른다.
        // 엔진이 Failed라 System이 없으면 새로 만든다.
        public int Reinitialize(IReadOnlyList<BootAttempt> attempts, string reason)
        {
            AudioThread.AssertMain("AudioEngine.Reinitialize");
            if (_status == EngineStatus.Disposed) return -1;
            if (!_system.hasHandle())
            {
                FMOD.RESULT created = FMOD.Factory.System_Create(out _system);
                if (created != FMOD.RESULT.OK)
                {
                    _system = default;
                    BootSummary = "재구성 실패 | System_Create " + created;
                    Debug.LogError("[Audio] " + BootSummary);
                    SetStatus(EngineStatus.Failed);
                    return -1;
                }
            }

            var failures = new List<string>();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < attempts.Count; i++)
            {
                _system.close();
                _systemMaster = default;
                string error = EngineConfigurator.TryInit(_system, attempts[i], out AudioOutputInfo actual);
                if (error == null) error = AdoptSystem(_system);
                if (error == null)
                {
                    InitMilliseconds = watch.Elapsed.TotalMilliseconds;
                    Adopted(attempts[i], actual, failures, "엔진 재구성(" + reason + ")");
                    return i;
                }
                failures.Add(attempts[i].Label + ": " + error);
            }

            InitMilliseconds = watch.Elapsed.TotalMilliseconds;
            BootSummary = "재구성 실패(" + reason + ") | " + string.Join(" | ", failures);
            Debug.LogError("[Audio] " + BootSummary);
            SystemCallbackHub.Uninstall(_system);
            _system.release();
            _system = default;
            _systemMaster = default;
            SetStatus(EngineStatus.Failed);
            return -1;
        }

        internal CallbackSnapshot Callbacks
        {
            get { return _callbacks; }
        }

        // Follow-Default에서 FMOD가 기본 장치를 바꾼 뒤(DEVICEREINITIALIZE) 표시용 장치 이름을 다시 읽는다.
        // 믹서 레이트는 setSoftwareFormat으로 고정했으므로 바뀌지 않는다.
        public void RefreshCurrentDevice()
        {
            if (!_system.hasHandle() || _currentOutput.Kind == AudioOutputKind.NoSound) return;
            if (_system.getDriver(out int driver) != FMOD.RESULT.OK) return;
            AudioDeviceInfo info;
            if (!Output.DriverLookup.TryRead(_system, driver, out info)) return;
            AudioOutputInfo o = _currentOutput;
            _currentOutput = new AudioOutputInfo(o.Kind, o.DeviceId, info.Name, o.SampleRate, o.BufferLength, o.BufferCount);
        }

        // 매 프레임 한 번(AudioEngineRunner, -1010). update 안에서 장치 콜백이 오고, 쌓인 오류와 FMOD 경고를 로그로 옮긴다.
        public void Update()
        {
            if (_system.hasHandle())
            {
                _system.update();
                SystemCallbackHub.Drain(_callbacks);
                ReportCallbacks();
            }
            else
            {
                _callbacks.ErrorCount = 0;
                _callbacks.DroppedErrors = 0;
                _callbacks.DeviceLost = false;
                _callbacks.DeviceListChanged = false;
                _callbacks.DeviceReinitialized = false;
            }
            FmodDebugBridge.Flush();
        }

        // 종료 1단계: 채널·Sound·ChannelGroup을 해제하기 전에 콜백부터 끊는다.
        public void DetachCallbacks()
        {
            SystemCallbackHub.Uninstall(_system);
        }

        // 종료 2단계: System release → FMOD Debug 원상 복구. 멱등이다.
        public void Shutdown()
        {
            if (_status == EngineStatus.Disposed) return;
            AudioThread.AssertMain("AudioEngine.Shutdown");

            if (_system.hasHandle())
            {
                SystemCallbackHub.Uninstall(_system);
                _system.release();
            }
            _system = default;
            _systemMaster = default;
            FmodDebugBridge.Uninstall();
            SetStatus(EngineStatus.Disposed);
        }

        private void Adopted(BootAttempt attempt, AudioOutputInfo actual, List<string> failures, string what)
        {
            _currentAttempt = attempt;
            _currentOutput = actual;
            _generation++;
            BootSummary = Describe(attempt.Label, failures);
            Debug.Log("[Audio] " + what + ": " + BootSummary);

            if (attempt.Kind == AudioOutputKind.NoSound)
            {
                Debug.LogWarning("[Audio] 소리 없이(NOSOUND) 동작합니다.");
                SetStatus(EngineStatus.Degraded);
            }
            else
            {
                SetStatus(EngineStatus.Running);
            }
        }

        private string AdoptSystem(FMOD.System system)
        {
            FMOD.RESULT result = system.getMasterChannelGroup(out FMOD.ChannelGroup master);
            if (result != FMOD.RESULT.OK) return "getMasterChannelGroup: " + result;
            _system = system;
            _systemMaster = master;
            return null;
        }

        private void ReportCallbacks()
        {
            int logged = _callbacks.ErrorCount;
            if (logged > MaxLoggedErrorsPerFrame) logged = MaxLoggedErrorsPerFrame;
            for (int i = 0; i < logged; i++)
            {
                Debug.LogWarning("[Audio] FMOD 오류 콜백: " + _callbacks.Errors[i] + " (" + _callbacks.ErrorSources[i] + ")");
            }
            int skipped = _callbacks.ErrorCount - logged + _callbacks.DroppedErrors;
            if (skipped > 0) Debug.LogWarning("[Audio] FMOD 오류 콜백 " + skipped + "건을 더 생략했습니다.");
            TotalErrors += _callbacks.ErrorCount + _callbacks.DroppedErrors;

            // 장치 변경 처리(재구성, 게임 중 일시정지, 곡 시계 리셋)는 출력 관리 단계(S2b)에서 붙인다.
            if (_callbacks.DeviceLost) Debug.LogWarning("[Audio] 출력 장치를 잃었습니다(DEVICELOST).");
            if (_callbacks.DeviceListChanged) Debug.Log("[Audio] 출력 장치 목록이 바뀌었습니다(DEVICELISTCHANGED).");
            if (_callbacks.DeviceReinitialized) Debug.Log("[Audio] 출력 장치가 다시 초기화되었습니다(DEVICEREINITIALIZE).");
        }

        private string Describe(string label, List<string> failures)
        {
            AudioOutputInfo o = _currentOutput;
            double blockMs = 0;
            if (o.SampleRate > 0) blockMs = o.BufferLength * 1000.0 / o.SampleRate;
            string text = string.Format("{0}, 출력 {1}, 장치 \"{2}\", {3}Hz, 버퍼 {4}x{5}(블록 {6:F2}ms), 세대 {7}, init {8:F1}ms",
                label, o.Kind, o.DeviceName, o.SampleRate, o.BufferLength, o.BufferCount, blockMs, _generation, InitMilliseconds);
            if (failures.Count > 0) text += " | 앞선 시도: " + string.Join(" | ", failures);
            return text;
        }

        private void SetStatus(EngineStatus status)
        {
            if (_status == status) return;
            _status = status;
            Action<EngineStatus> handler = StatusChanged;
            if (handler == null) return;
            try
            {
                handler(status);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
