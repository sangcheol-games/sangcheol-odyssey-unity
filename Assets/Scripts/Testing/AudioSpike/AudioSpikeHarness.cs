#if SCO_AUDIO_HARNESS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.Testing.AudioSpike
{
    // S-0.5 최소 스파이크 하네스. AudioSpikeScene의 빈 GameObject에 붙여 쓴다.
    // 화면 버튼으로 SP1~SP4, SP11, SP15를 실행하고 결과를 persistentDataPath/audio_spike/에 남긴다.
    // 이 코드는 S1에서 모듈 하네스로 교체하고 삭제한다.
    public sealed class AudioSpikeHarness : MonoBehaviour
    {
        private static readonly uint[] BufferLengths = { 64, 128, 256, 480, 512, 1024 };
        private static readonly int[] BufferCounts = { 2, 4 };
        private static readonly int[] FrameRates = { 60, 144, -1 };

        private FMOD.OUTPUTTYPE _output = FMOD.OUTPUTTYPE.WASAPI;
        private int _driverIndex;
        private int _lengthIndex = 2;
        private int _countIndex = 1;
        private int _frameRateIndex;
        private bool _longClockRun = true;
        private bool _gcStress;

        private List<string> _drivers = new List<string>();
        private string _driverError;
        private SpikeFmodSystem _system;
        private string _systemError = "";
        private string _status = "대기";
        private bool _busy;
        private Vector2 _scroll;

        private readonly SpikeClockRecorder _clockRecorder = new SpikeClockRecorder();
        private readonly SpikeInputRecorder _inputRecorder = new SpikeInputRecorder();
        private float _inputEndTime;

        private void OnEnable()
        {
#if UNITY_EDITOR
            // 플레이 중 재컴파일이 일어나면 도메인이 내려가기 전에 FMOD를 먼저 해제한다.
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif
            ReleaseAll();
        }

#if UNITY_EDITOR
        // SP1 재컴파일 확인: 리로드 직전에 시스템이 켜져 있었는지 요약에 남긴 뒤 해제한다.
        private void OnBeforeAssemblyReload()
        {
            string context = "시스템 꺼짐";
            if (_system != null) context = _system.Describe();
            SpikeReport.Summary("SP1-reload", SpikeReport.Result.Info, "Play 중 리로드 직전 FMOD 해제", "시스템 켜짐: " + (_system != null), context);
            ReleaseAll();
        }
#endif

        private void Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FrameRates[_frameRateIndex];
            RefreshDrivers();
            SpikeLifecycle.BootCheck(CurrentConfig());
            _status = "SP1 부팅 확인을 기록했습니다. 요약 파일을 확인하세요.";
        }

        private void Update()
        {
            if (_system != null) _system.CoreSystem.update();

            if (_clockRecorder.IsRecording)
            {
                bool done = _clockRecorder.Sample(Time.unscaledDeltaTime);
                _status = "SP3 기록 중 " + (int)_clockRecorder.ElapsedSeconds + "초";
                if (done) FinishClock();
            }

            if (_inputRecorder.IsRecording)
            {
                _inputRecorder.SampleMapper();
                int remaining = (int)(_inputEndTime - Time.unscaledTime);
                _status = "SP11: 스페이스를 일정하게 계속 누르세요. 남은 시간 " + remaining + "초, 탭 " + _inputRecorder.PressCount + "회";
                if (Time.unscaledTime >= _inputEndTime)
                {
                    _inputRecorder.Finish();
                    _busy = false;
                    _status = "SP11 완료";
                }
            }
        }

        private void LateUpdate()
        {
            if (!_clockRecorder.IsRecording) return;
            bool done = _clockRecorder.Sample(0f);
            if (done) FinishClock();
        }

        private void OnGUI()
        {
            float scale = Screen.height / 720f;
            if (scale < 1f) scale = 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUI.skin.label.richText = true;

            GUILayout.BeginArea(new Rect(10, 10, Screen.width / scale - 20, Screen.height / scale - 20));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("<b>오디오 스파이크 하네스 (S-0.5)</b>");
            double fps = 1.0 / Time.smoothDeltaTime;
            string fpsText = "현재 fps: " + SpikeReport.Num(fps, "0");
            if (fps < 30) fpsText = "<color=orange>" + fpsText + " (30 미만: SP3 프레임 기록과 SP11 결과의 신뢰도가 낮습니다)</color>";
            GUILayout.Label(fpsText);
            GUILayout.Label("상태: " + _status);
            GUILayout.Label("결과 폴더: " + SpikeReport.Folder);

            DrawOutputSettings();
            GUILayout.Space(8);
            DrawSystemControls();
            GUILayout.Space(8);
            DrawSpikeButtons();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawOutputSettings()
        {
            GUI.enabled = !_busy && _system == null;
            GUILayout.Label("<b>출력 설정</b> (시스템이 꺼져 있을 때만 바꿀 수 있음)");

            GUILayout.BeginHorizontal();
            GUILayout.Label("출력 타입: " + _output, GUILayout.Width(220));
            if (GUILayout.Button("WASAPI")) SelectOutput(FMOD.OUTPUTTYPE.WASAPI);
            if (GUILayout.Button("ASIO")) SelectOutput(FMOD.OUTPUTTYPE.ASIO);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("장치: " + DriverLabel(), GUILayout.Width(520));
            if (GUILayout.Button("◀")) ChangeDriver(-1);
            if (GUILayout.Button("▶")) ChangeDriver(1);
            if (GUILayout.Button("목록 새로고침")) RefreshDrivers();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("버퍼: " + BufferLengths[_lengthIndex] + " x " + BufferCounts[_countIndex], GUILayout.Width(220));
            for (int i = 0; i < BufferLengths.Length; i++)
            {
                if (GUILayout.Button(BufferLengths[i].ToString())) _lengthIndex = i;
            }
            for (int i = 0; i < BufferCounts.Length; i++)
            {
                if (GUILayout.Button("x" + BufferCounts[i])) _countIndex = i;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawSystemControls()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("<b>시스템</b> (SP3, SP4, SP15는 시스템을 켠 뒤 실행)");
            GUILayout.BeginHorizontal();
            if (_system == null)
            {
                if (GUILayout.Button("시스템 켜기", GUILayout.Width(160))) StartSystem();
                GUILayout.Label("꺼짐 " + _systemError);
            }
            else
            {
                if (GUILayout.Button("시스템 끄기", GUILayout.Width(160))) StopSystem();
                GUILayout.Label("켜짐: " + _system.Describe() + ", 블록 " + SpikeReport.Ms(_system.BlockSeconds));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("FPS 목표: " + FrameRateLabel(), GUILayout.Width(220));
            for (int i = 0; i < FrameRates.Length; i++)
            {
                string label = FrameRates[i].ToString();
                if (FrameRates[i] < 0) label = "무제한";
                if (GUILayout.Button(label)) SelectFrameRate(i);
            }
            GUILayout.Label("현재 " + SpikeReport.Num(1.0 / Time.smoothDeltaTime, "0") + " fps");
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawSpikeButtons()
        {
            GUI.enabled = !_busy;
            GUILayout.Label("<b>스파이크</b>");

            if (GUILayout.Button("SP1: 시스템 생성·해제 20회 (현재 출력 설정)")) RunExclusive(SpikeLifecycle.RunCycles(CurrentConfig(), 20, SetStatus));
            if (GUILayout.Button("SP2: 아파트먼트 확인 + ASIO init·close→init 각 10회 (현재 장치)")) RunExclusive(SpikeAsio.RunInitCycles(AsioConfig(), 10, SetStatus));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SP3: 시계 기록 시작(처음 2초는 화면이 멈춤)", GUILayout.Width(360))) StartClock();
            _longClockRun = GUILayout.Toggle(_longClockRun, "5분(끄면 1분)");
            _gcStress = GUILayout.Toggle(_gcStress, "GC 부하(초당 50MB)");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("SP4: 예약·일시정지 20회 (루프백 녹음을 먼저 시작하세요)")) RunWithSystem(new SpikeSchedule().Run(_system, 20, SetStatus));
            if (GUILayout.Button("SP11: 입력 기록 60초 (스페이스를 일정하게 누르기)")) StartInput();
            if (GUILayout.Button("SP15: 콜백 스레드 기록")) RunWithSystem(SpikeCallbacks.Run(_system, SetStatus));

            GUI.enabled = true;
            if (_busy && GUILayout.Button("중단(진행 중인 기록 마무리)")) StopAll();
        }

        private SpikeOutputConfig CurrentConfig()
        {
            var config = new SpikeOutputConfig();
            config.Output = _output;
            config.DriverIndex = _driverIndex;
            config.BufferLength = BufferLengths[_lengthIndex];
            config.BufferCount = BufferCounts[_countIndex];
            return config;
        }

        private SpikeOutputConfig AsioConfig()
        {
            SpikeOutputConfig config = CurrentConfig();
            if (_output != FMOD.OUTPUTTYPE.ASIO)
            {
                config.DriverIndex = 0;
                config.BufferCount = 2;
            }
            config.Output = FMOD.OUTPUTTYPE.ASIO;
            return config;
        }

        private void SelectOutput(FMOD.OUTPUTTYPE output)
        {
            _output = output;
            _driverIndex = 0;
            if (output == FMOD.OUTPUTTYPE.ASIO) _countIndex = 0;
            else _countIndex = 1;
            RefreshDrivers();
        }

        private void RefreshDrivers()
        {
            _drivers = SpikeAsio.ListDrivers(_output, out _driverError);
            if (_driverIndex >= _drivers.Count) _driverIndex = 0;
        }

        private void ChangeDriver(int delta)
        {
            if (_drivers.Count == 0) return;
            _driverIndex = (_driverIndex + delta + _drivers.Count) % _drivers.Count;
        }

        private string DriverLabel()
        {
            if (_driverError != null) return "목록 오류: " + _driverError;
            if (_drivers.Count == 0) return "장치 없음";
            return "[" + _driverIndex + "] " + _drivers[_driverIndex];
        }

        private string FrameRateLabel()
        {
            int rate = FrameRates[_frameRateIndex];
            if (rate < 0) return "무제한";
            return rate.ToString();
        }

        private void SelectFrameRate(int index)
        {
            _frameRateIndex = index;
            Application.targetFrameRate = FrameRates[index];
        }

        private void StartSystem()
        {
            _system = SpikeFmodSystem.Create(CurrentConfig(), out string error);
            if (_system == null) _systemError = "(" + error + ")";
            else _systemError = "";
        }

        private void StopSystem()
        {
            if (_system == null) return;
            SpikeCallbacks.Shutdown(_system);
            _system.Dispose();
            _system = null;
        }

        // SP1, SP2처럼 시스템을 직접 만들고 지우는 스파이크는 켜 둔 시스템을 먼저 끈다(ASIO는 프로세스당 하나).
        private void RunExclusive(IEnumerator routine)
        {
            StopSystem();
            StartCoroutine(RunBusy(routine));
        }

        private void RunWithSystem(IEnumerator routine)
        {
            if (_system == null)
            {
                _status = "먼저 '시스템 켜기'를 누르세요.";
                return;
            }
            StartCoroutine(RunBusy(routine));
        }

        private IEnumerator RunBusy(IEnumerator routine)
        {
            _busy = true;
            yield return routine;
            _busy = false;
        }

        private void StartClock()
        {
            if (_system == null)
            {
                _status = "먼저 '시스템 켜기'를 누르세요.";
                return;
            }
            double duration = 60.0;
            if (_longClockRun) duration = 300.0;
            _busy = true;
            _clockRecorder.Begin(_system, duration, _gcStress);
        }

        private void FinishClock()
        {
            _clockRecorder.Finish();
            _busy = false;
            _status = "SP3 완료";
        }

        private void StartInput()
        {
            _busy = true;
            _inputEndTime = Time.unscaledTime + 60f;
            _inputRecorder.Begin(FrameRateLabel());
        }

        // 진행 중인 기록은 마무리하고, 재생 중인 소리가 남지 않도록 시스템도 끈다.
        private void StopAll()
        {
            ReleaseAll();
            _status = "중단됨. 다시 하려면 '시스템 켜기'부터 누르세요.";
        }

        private void SetStatus(string text)
        {
            _status = text;
        }

        private void ReleaseAll()
        {
            StopAllCoroutines();
            if (_clockRecorder.IsRecording) _clockRecorder.Finish();
            if (_inputRecorder.IsRecording) _inputRecorder.Finish();
            StopSystem();
            _busy = false;
        }
    }
}
#endif
