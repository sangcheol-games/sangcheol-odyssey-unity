#if SCO_AUDIO_HARNESS
using System;
using System.Collections;
using System.IO;
using SCOdyssey.Audio;
using SCOdyssey.Audio.Diagnostics;
using SCOdyssey.Audio.Engine;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio.Hosting;
using UnityEngine;

namespace SCOdyssey.Testing.AudioHarness
{
    // 오디오 모듈 하네스. AudioSpikeScene의 빈 GameObject에 붙여 쓴다(S-0.5 하네스의 파일 GUID를 이어받아 씬은 그대로다).
    // 새 오디오 모듈을 게임 없이 단독으로 설치해 부팅, 원샷, 볼륨, 포커스 음소거, 수명주기를 시험하고 요약 파일에 남긴다.
    // 곡 재생·재구성·프리뷰 확인은 HarnessSongChecks(S2a), 출력 적용·장치 사건은 HarnessOutputChecks(S2b),
    // 탭 테스트와 Synthetic 확인은 HarnessTimingChecks(S3).
    public sealed class AudioModuleHarness : MonoBehaviour
    {
        private const string ClickFile = "harness_click.wav";
        private const int Cycles = 20;
        private static readonly int[] BufferLengths = { 256, 480, 512, 1024 };
        private static readonly int[] BufferCounts = { 2, 4 };

        private AudioOutputKind _kind = AudioOutputKind.Wasapi;
        private int _lengthIndex;
        private int _countIndex = 1;
        private bool _safeMode;
        private bool _installDisabled;   // 설치 실패 대비 경로(InstallDisabled) 확인용
        private bool _playInBackground = true;
        private bool _pauseSongOnFocusLoss = true;

        private AudioModule _module;
        private OneShotId _click;
        private string _status = "대기";
        private bool _busy;
        private Vector2 _scroll;
        private HarnessSongChecks _songs;
        private HarnessOutputChecks _outputs;
        private HarnessTimingChecks _timing;

        private static string HitSoundFolder
        {
            get { return Path.Combine(HarnessReport.Folder, "hitsound"); }
        }

        private static string MusicFolder
        {
            get { return Path.Combine(HarnessReport.Folder, "music"); }
        }

        private bool IsInstalled
        {
            get { return _module != null && !_module.IsShutDown; }
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RecordReload;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RecordReload;
#endif
        }

        private void Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Directory.CreateDirectory(HitSoundFolder);
            HarnessWav.WriteClick(Path.Combine(HitSoundFolder, ClickFile), 48000);
            HarnessSongChecks.PrepareFiles(MusicFolder);
            _songs = new HarnessSongChecks(CurrentModule, SetStatus);
            _outputs = new HarnessOutputChecks(CurrentModule, SetStatus);
            BootCheck();
            _timing = new HarnessTimingChecks(_songs, SetStatus, gameObject);
        }

        private void OnDestroy()
        {
            ShutdownModule();
            if (_timing != null) _timing.Dispose();
            _timing = null;
        }

        private void Update()
        {
            // 확인 버튼이 도는 동안(SP6의 재구성 100회 등)에는 사건 기록을 멈춘다. 끝난 뒤의 상태를 기준으로 다시 본다.
            if (_outputs != null && IsInstalled) _outputs.Watch(_songs.Session, _busy);
            if (_timing != null) _timing.Tick();
        }

        // SP1: Play(또는 실행 파일 실행)마다 한 줄. 설치한 모듈은 켜 둔 채로 둔다(재컴파일 확인용).
        private void BootCheck()
        {
            bool runtimeManagerBefore = RuntimeManagerGuard.IsInitialized;
            InstallModule();
            bool running = _module.Engine.Status == EngineStatus.Running;
            if (_click.IsValid) _module.OneShots.Play(_click);

            bool pass = running && _click.IsValid && !runtimeManagerBefore && !RuntimeManagerGuard.IsInitialized;
            string measured = "상태 " + _module.Engine.Status + ", init " + HarnessReport.Num(_module.InitMilliseconds, "0.0") + "ms, 원샷 등록 " + _click.IsValid;
            HarnessReport.Summary("SP1-boot", HarnessReport.PassIf(pass), "Running, 원샷 등록, RM(Studio) 미초기화", measured, Describe());
            _status = "SP1 부팅 확인을 기록했습니다.";
        }

        private void InstallModule()
        {
            ShutdownModule();
            var options = new AudioModuleOptions();
            options.Output = new AudioOutputRequest(_kind, Guid.Empty, "", BufferLengths[_lengthIndex], BufferCounts[_countIndex]);
            options.SafeMode = _safeMode;
            options.HitSoundFolder = HitSoundFolder;
            options.MusicFolder = MusicFolder;
            options.PlayInBackground = ReadPlayInBackground;
            options.PauseSongOnFocusLoss = _pauseSongOnFocusLoss;
            options.EnforceRuntimeManagerGuard = true;

            if (_installDisabled) _module = AudioModuleInstaller.InstallDisabled(gameObject, options, "하네스: 무음 모듈 확인");
            else _module = AudioModuleInstaller.Install(gameObject, options);
            _click = _module.OneShots.Register(ClickFile);
        }

        private void ShutdownModule()
        {
            if (_timing != null) _timing.DetachBinding();
            if (_songs != null) _songs.Detach();
            if (_module == null) return;
            _module.Shutdown();
            _module = null;
            _click = OneShotId.None;
        }

        private bool ReadPlayInBackground()
        {
            return _playInBackground;
        }

        private AudioModule CurrentModule()
        {
            return _module;
        }

        private void SetStatus(string text)
        {
            _status = text;
        }

        private IEnumerator RunBusy(IEnumerator routine)
        {
            _busy = true;
            yield return routine;
            _busy = false;
        }

        // SP1: 설치 → 원샷 → 3프레임 → 종료를 반복한다. 끝나면 다시 설치해 둔다.
        private IEnumerator RunCycles()
        {
            _busy = true;
            ShutdownModule();
            FMOD.Memory.GetStats(out int memoryBefore, out _);
            int failures = 0;
            int idempotencyFailures = 0;
            double maxInit = 0;
            string lastError = "";

            for (int i = 0; i < Cycles; i++)
            {
                _status = string.Format("SP1 반복 {0}/{1}", i + 1, Cycles);
                InstallModule();
                if (_module.Engine.Status != EngineStatus.Running)
                {
                    failures++;
                    lastError = _module.BootSummary;
                }
                else if (!_click.IsValid)
                {
                    failures++;
                    lastError = "원샷 등록 실패";
                }
                else
                {
                    _module.OneShots.Play(_click);
                    if (!_module.OneShots.Register(ClickFile).Equals(_click)) idempotencyFailures++;
                }
                if (_module.InitMilliseconds > maxInit) maxInit = _module.InitMilliseconds;

                for (int frame = 0; frame < 3; frame++) yield return null;
                ShutdownModule();
                yield return null;
            }

            FMOD.Memory.GetStats(out int memoryAfter, out _);
            int memoryDelta = memoryAfter - memoryBefore;
            bool runtimeManager = RuntimeManagerGuard.IsInitialized;
            bool pass = failures == 0 && idempotencyFailures == 0 && !runtimeManager && memoryDelta < 1024 * 1024;
            string measured = string.Format("실패 {0}/{1}, 원샷 멱등 실패 {2}, 최대 init {3}ms, FMOD 메모리 변화 {4}KB, RM(Studio) 초기화: {5}",
                failures, Cycles, idempotencyFailures, HarnessReport.Num(maxInit, "0.0"), memoryDelta / 1024, runtimeManager);
            if (failures > 0) measured += ", 마지막 오류: " + lastError;

            InstallModule();
            HarnessReport.Summary("SP1-cycles", HarnessReport.PassIf(pass), "실패 0, 원샷 멱등, RM(Studio) 미초기화, 메모리 변화 < 1MB", measured, Describe());
            _status = "SP1 반복 완료";
            _busy = false;
        }

#if UNITY_EDITOR
        // SP1 재컴파일 확인: 리로드 직전에 모듈이 동작 중이었는지 남긴다. 모듈 자체는 EditorAudioLifecycle이 종료한다.
        private void RecordReload()
        {
            bool running = IsInstalled && _module.Engine.Status == EngineStatus.Running;
            HarnessReport.Summary("SP1-reload", HarnessReport.Result.Info, "Play 중 리로드 직전 모듈 동작", "모듈 동작 중: " + running, Describe());
        }
#endif

        private string Describe()
        {
            if (!IsInstalled) return "모듈 없음";
            AudioOutputInfo o = _module.Engine.CurrentOutput;
            return string.Format("출력: {0}, 장치: {1}, 레이트: {2}, 버퍼: {3}x{4}, 세대: {5}",
                o.Kind, o.DeviceName, o.SampleRate, o.BufferLength, o.BufferCount, _module.Engine.Generation);
        }

        private void OnGUI()
        {
            float scale = Screen.height / 720f;
            if (scale < 1f) scale = 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.skin.label.richText = true;

            GUILayout.BeginArea(new Rect(10, 10, Screen.width / scale - 460, Screen.height / scale - 20));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>오디오 모듈 하네스 (S3)</b>");
            GUILayout.Label("fps: " + HarnessReport.Num(1.0 / Time.smoothDeltaTime, "0") + ", 상태: " + _status);
            GUILayout.Label("결과 폴더: " + HarnessReport.Folder);
            GUILayout.Label("RM(Studio) 초기화: " + RuntimeManagerGuard.IsInitialized);

            GUILayout.Space(8);
            DrawOutputSettings();
            GUILayout.Space(8);
            DrawModuleControls();
            GUILayout.Space(8);
            DrawPlayback();
            GUILayout.Space(8);
            DrawSong();
            GUILayout.Space(8);
            DrawOutputApply();
            GUILayout.Space(8);
            DrawTiming();
            GUILayout.Space(8);
            DrawChecks();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawOutputSettings()
        {
            GUI.enabled = !_busy && !IsInstalled;
            GUILayout.Label("<b>출력 요청</b> (모듈을 끈 뒤 바꾸고 다시 설치)");

            GUILayout.BeginHorizontal();
            GUILayout.Label("타입: " + _kind, GUILayout.Width(200));
            if (GUILayout.Button("WASAPI")) SelectKind(AudioOutputKind.Wasapi);
            if (GUILayout.Button("ASIO")) SelectKind(AudioOutputKind.Asio);
            if (GUILayout.Button("NOSOUND")) SelectKind(AudioOutputKind.NoSound);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("버퍼: " + BufferLengths[_lengthIndex] + " x " + BufferCounts[_countIndex], GUILayout.Width(200));
            for (int i = 0; i < BufferLengths.Length; i++)
            {
                if (GUILayout.Button(BufferLengths[i].ToString())) _lengthIndex = i;
            }
            for (int i = 0; i < BufferCounts.Length; i++)
            {
                if (GUILayout.Button("x" + BufferCounts[i])) _countIndex = i;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _safeMode = GUILayout.Toggle(_safeMode, "안전 모드(요청 구성 건너뜀)", GUILayout.Width(240));
            _installDisabled = GUILayout.Toggle(_installDisabled, "무음 모듈(설치 실패 대비)", GUILayout.Width(200));
#if UNITY_EDITOR
            AsioPolicy.AllowInEditor = GUILayout.Toggle(AsioPolicy.AllowInEditor, "에디터에서 ASIO 허용", GUILayout.Width(200));
#endif
            GUILayout.Label("ASIO 지원(x64): " + AsioPolicy.IsSupported);
            GUILayout.EndHorizontal();
            // SP10처럼 Windows 설정을 눌러야 하는 확인에서만 끈다(게임은 항상 켠다).
            _pauseSongOnFocusLoss = GUILayout.Toggle(_pauseSongOnFocusLoss, "포커스 잃으면 곡 일시정지(SP10 확인 때만 끄고 다시 설치)");
            GUI.enabled = true;
        }

        private void DrawModuleControls()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("<b>모듈</b>");
            GUILayout.BeginHorizontal();
            if (IsInstalled)
            {
                // 버튼이 같은 OnGUI 안에서 모듈을 끌 수 있으므로 다시 확인한다.
                if (GUILayout.Button("종료", GUILayout.Width(160))) ShutdownModule();
                if (IsInstalled) GUILayout.Label(_module.Engine.Status + " / " + Describe());
                else GUILayout.Label("꺼짐");
            }
            else
            {
                if (GUILayout.Button("설치", GUILayout.Width(160))) InstallModule();
                GUILayout.Label("꺼짐");
            }
            GUILayout.EndHorizontal();
            if (IsInstalled) GUILayout.Label("부팅: " + _module.BootSummary);
            AudioOverlay.Visible = GUILayout.Toggle(AudioOverlay.Visible, "오디오 오버레이");
            GUI.enabled = true;
        }

        private void DrawPlayback()
        {
            if (!IsInstalled) return;
            GUI.enabled = !_busy;
            GUILayout.Label("<b>재생</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("원샷 재생", GUILayout.Width(160))) _module.OneShots.Play(_click);
            GUILayout.Label("원샷 id 유효: " + _click.IsValid);
            GUILayout.EndHorizontal();

            DrawVolume("Master", _module.Mixer.Master);
            DrawVolume("HitSound", _module.Mixer.HitSound);

            _playInBackground = GUILayout.Toggle(_playInBackground, "백그라운드 재생 (끄고 다른 창을 누르면 음소거되어야 함)");
            GUI.enabled = true;
        }

        private static void DrawVolume(string label, IMixBus bus)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + " " + HarnessReport.Num(bus.Volume, "0.00"), GUILayout.Width(160));
            bus.Volume = GUILayout.HorizontalSlider(bus.Volume, 0f, 1f, GUILayout.Width(240));
            GUILayout.EndHorizontal();
        }

        private void DrawSong()
        {
            if (!IsInstalled || _songs == null) return;
            GUI.enabled = !_busy;
            GUILayout.Label("<b>곡</b> (0.5초 클릭 트랙: 곡은 왼쪽, 메트로놈은 오른쪽)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("로드", GUILayout.Width(80))) StartCoroutine(_songs.Load());
            if (GUILayout.Button("시작", GUILayout.Width(80))) _songs.Start();
            if (GUILayout.Button("일시정지", GUILayout.Width(80))) _songs.Pause();
            if (GUILayout.Button("재개", GUILayout.Width(80))) _songs.Resume();
            if (GUILayout.Button("정지", GUILayout.Width(80))) _songs.Stop();
            if (GUILayout.Button("강제 재구성", GUILayout.Width(120))) _module.Reinitialize();
            GUILayout.EndHorizontal();

            ISongSession session = _songs.Session;
            if (session != null)
            {
                SongFrame frame = session.Clock.Frame;
                GUILayout.Label(string.Format("상태 {0}, 곡 시각 {1}, 흐름 {2}, Epoch {3}, 일시정지 사유 {4}, 음원 종료 {5}",
                    session.State, HarnessReport.Num(frame.SongTime, "0.000"), frame.IsRunning, frame.Epoch, session.PauseReason, session.IsAudioFinished));
                GUILayout.Label("이벤트: " + _songs.RecentEvents);
            }
            GUILayout.Label("마지막 로드: " + _songs.LastLoad);
            _module.Metronome.Enabled = GUILayout.Toggle(_module.Metronome.Enabled, "메트로놈(오른쪽)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("로비 BGM 재생(루프)", GUILayout.Width(160))) _module.Music.Lobby.PlayAsync(HarnessSongChecks.TrackFile, true, System.Threading.CancellationToken.None).Forget();
            if (GUILayout.Button("로비 BGM 정지", GUILayout.Width(120))) _module.Music.Lobby.Stop();
            GUILayout.Label("로비 재생 중: " + _module.Music.Lobby.IsPlaying + ", 프리뷰 재생 중: " + _module.Music.Preview.IsPlaying);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawOutputApply()
        {
            if (!IsInstalled || _outputs == null) return;
            GUI.enabled = !_busy;
            GUILayout.Label("<b>출력 적용</b> (ApplyAsync: close→init, 폴백 포함)");

            GUILayout.BeginHorizontal();
            GUILayout.Label("타입: " + _outputs.Kind, GUILayout.Width(200));
            if (GUILayout.Button("WASAPI")) SelectApplyKind(AudioOutputKind.Wasapi);
            if (GUILayout.Button("ASIO")) SelectApplyKind(AudioOutputKind.Asio);
            GUILayout.Label("지원: " + _module.Output.IsSupported(_outputs.Kind));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("장치: " + _outputs.DeviceLabel, GUILayout.Width(520));
            if (GUILayout.Button("◀")) _outputs.ChangeDevice(-1);
            if (GUILayout.Button("▶")) _outputs.ChangeDevice(1);
            if (GUILayout.Button("목록 새로고침")) StartCoroutine(RunBusy(_outputs.RefreshDevices()));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("버퍼: " + _outputs.BufferLength + " x " + _outputs.BufferCount, GUILayout.Width(200));
            for (int i = 0; i < BufferLengths.Length; i++)
            {
                if (GUILayout.Button(BufferLengths[i].ToString())) _outputs.BufferLength = BufferLengths[i];
            }
            for (int i = 0; i < BufferCounts.Length; i++)
            {
                if (GUILayout.Button("x" + BufferCounts[i])) _outputs.BufferCount = BufferCounts[i];
            }
            if (GUILayout.Button("적용", GUILayout.Width(100))) StartCoroutine(RunBusy(_outputs.Apply()));
            GUILayout.EndHorizontal();
            GUILayout.Label("마지막 적용: " + _outputs.LastApply);
            GUI.enabled = true;
        }

        private void DrawTiming()
        {
            if (!IsInstalled || _timing == null) return;
            GUI.enabled = !_busy;
            GUILayout.Label("<b>판정 타이밍</b> (레인 키 Q, A, ', / → 새 입력 소스 → JudgementDriver → GameplayTimingBinding)");

            GUILayout.BeginHorizontal();
            string target = "무제한";
            if (Application.targetFrameRate > 0) target = Application.targetFrameRate.ToString();
            GUILayout.Label("목표 fps: " + target, GUILayout.Width(200));
            if (GUILayout.Button("60")) Application.targetFrameRate = 60;
            if (GUILayout.Button("144")) Application.targetFrameRate = 144;
            if (GUILayout.Button("무제한")) Application.targetFrameRate = -1;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("판정 싱크 단계(곡 시작 때 래치): " + _timing.JudgmentOffsetSteps, GUILayout.Width(280));
            if (GUILayout.Button("-1")) _timing.JudgmentOffsetSteps--;
            if (GUILayout.Button("+1")) _timing.JudgmentOffsetSteps++;
            if (GUILayout.Button("0")) _timing.JudgmentOffsetSteps = 0;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SCOdyssey.Game.Timing.JudgementDriver.OverlayVisible = GUILayout.Toggle(SCOdyssey.Game.Timing.JudgementDriver.OverlayVisible, "타이밍 오버레이", GUILayout.Width(200));
            Application.runInBackground = GUILayout.Toggle(Application.runInBackground, "runInBackground (SP-IN alt-tab을 ON/OFF 각각)");
            GUILayout.EndHorizontal();
            GUILayout.Label(_timing.Describe());
            GUI.enabled = true;
        }

        private void SelectApplyKind(AudioOutputKind kind)
        {
            _outputs.Kind = kind;
            if (kind == AudioOutputKind.Asio) _outputs.BufferCount = 2;
            else _outputs.BufferCount = 4;
            StartCoroutine(RunBusy(_outputs.RefreshDevices()));
        }

        private void DrawChecks()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("<b>확인</b>");
            if (GUILayout.Button("SP1: 설치·원샷·종료 " + Cycles + "회 (현재 출력 요청)")) StartCoroutine(RunCycles());
            if (IsInstalled && _songs != null)
            {
                if (GUILayout.Button("SP4: 곡 일시정지·재개 21회 (루프백 녹음을 먼저 시작하세요, 약 80초)")) StartCoroutine(RunBusy(_songs.RunSp4()));
                if (GUILayout.Button("재구성: Ready·Starting·Playing·Paused에서 강제 재구성")) StartCoroutine(RunBusy(_songs.RunReconfigure()));
                if (GUILayout.Button("프리뷰: 한 프레임에 5번 요청")) StartCoroutine(RunBusy(_songs.RunPreviewSupersede()));
                if (GUILayout.Button("SP6: 로비 BGM 재생 중 설정 적용 50회 + close→init 50회 (현재 출력 적용 설정)")) StartCoroutine(RunBusy(_outputs.RunSp6(_click)));
                if (GUILayout.Button("SP9: 로비 BGM 재생 중 WASAPI·ASIO 장치 목록 3회")) StartCoroutine(RunBusy(_outputs.RunSp9()));
                GUILayout.Label("SP10: USB 분리·재연결, 기본 장치·형식 변경 등을 하면 세대·상태가 바뀔 때마다 요약에 한 줄씩 남는다.");
            }
            if (IsInstalled && _timing != null)
            {
                if (GUILayout.Button("SP11: 탭 테스트 1분 (현재 목표 fps, 클릭에 맞춰 레인 키)")) StartCoroutine(RunBusy(_timing.RunTapTest()));
                if (GUILayout.Button("SP11: 매퍼 장기 변동 기록 (하네스를 30분 이상 켜 둔 뒤)")) _timing.ReportLongRun();
                if (GUILayout.Button("SP-IN: 레인을 누른 채 입력 맵 끄기 (3초 뒤 자동)")) StartCoroutine(RunBusy(_timing.RunMapDisable()));
                if (GUILayout.Button("SP-IN: 레인을 누른 채 alt-tab (30초 안에)")) StartCoroutine(RunBusy(_timing.RunFocusLoss()));
            }
            GUI.enabled = true;
        }

        private void SelectKind(AudioOutputKind kind)
        {
            _kind = kind;
            if (kind == AudioOutputKind.Asio) _countIndex = 0;
            else _countIndex = 1;
        }
    }
}
#endif
