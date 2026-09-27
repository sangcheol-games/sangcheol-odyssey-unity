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
    // 곡 재생·재구성·프리뷰 확인은 HarnessSongChecks(S2a). 출력 변경(S2b), 탭 테스트(S3) 메뉴는 해당 단계에서 붙인다.
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
        private bool _playInBackground = true;

        private AudioModule _module;
        private OneShotId _click;
        private string _status = "대기";
        private bool _busy;
        private Vector2 _scroll;
        private HarnessSongChecks _songs;

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
            BootCheck();
        }

        private void OnDestroy()
        {
            ShutdownModule();
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
            options.EnforceRuntimeManagerGuard = true;

            _module = AudioModuleInstaller.Install(gameObject, options);
            _click = _module.OneShots.Register(ClickFile);
        }

        private void ShutdownModule()
        {
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

            GUILayout.Label("<b>오디오 모듈 하네스 (S2a)</b>");
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
#if UNITY_EDITOR
            AsioPolicy.AllowInEditor = GUILayout.Toggle(AsioPolicy.AllowInEditor, "에디터에서 ASIO 허용", GUILayout.Width(200));
#endif
            GUILayout.Label("ASIO 지원(x64): " + AsioPolicy.IsSupported);
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawModuleControls()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("<b>모듈</b>");
            GUILayout.BeginHorizontal();
            if (IsInstalled)
            {
                if (GUILayout.Button("종료", GUILayout.Width(160))) ShutdownModule();
                GUILayout.Label(_module.Engine.Status + " / " + Describe());
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
