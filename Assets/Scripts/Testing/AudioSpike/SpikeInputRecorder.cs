#if SCO_AUDIO_HARNESS
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP11: 키 입력 시각(ctx.time)이 실제로 처리되는 시각보다 얼마나 늦게 찍히는지(프레임 양자화)를 잰다.
    // 함께 Input System 시간축 → QPC 변환 오프셋이 얼마나 흔들리는지 본다(매퍼 방식 검증).
    public sealed class SpikeInputRecorder
    {
        private const double MaxReadSeconds = 50e-6;
        private static readonly double QpcToSeconds = 1.0 / Stopwatch.Frequency;

        private InputAction _action;
        private StreamWriter _csv;
        private readonly List<double> _lagSeconds = new List<double>();
        private readonly List<double> _offsets = new List<double>();
        private string _fpsLabel = "";

        public bool IsRecording { get; private set; }
        public int PressCount { get { return _lagSeconds.Count; } }

        public void Begin(string fpsLabel)
        {
            _fpsLabel = fpsLabel;
            _lagSeconds.Clear();
            _offsets.Clear();
            _csv = SpikeReport.OpenCsv("sp11_input_" + fpsLabel, "frame,ctx_time,callback_realtime,callback_qpc,lag_ms");

            _action = new InputAction("SpikeTap", InputActionType.Button, "<Keyboard>/space");
            _action.performed += OnPerformed;
            _action.Enable();
            IsRecording = true;
        }

        // 매 프레임 Update에서 부른다. (QPC, Input System 현재 시각, QPC)로 두 시간축의 오프셋을 잰다.
        public void SampleMapper()
        {
            if (!IsRecording) return;
            long q1 = Stopwatch.GetTimestamp();
            double inputTime = InputState.currentTime;
            long q2 = Stopwatch.GetTimestamp();
            if ((q2 - q1) * QpcToSeconds > MaxReadSeconds) return;
            double mid = (q1 + q2) * 0.5 * QpcToSeconds;
            _offsets.Add(mid - inputTime);
        }

        public void Finish()
        {
            if (!IsRecording) return;
            IsRecording = false;
            _action.performed -= OnPerformed;
            _action.Disable();
            _action.Dispose();
            _action = null;
            _csv.Dispose();
            _csv = null;

            if (_lagSeconds.Count < 10)
            {
                SpikeReport.Summary("SP11", SpikeReport.Result.Fail, "탭 10회 이상", "탭 " + _lagSeconds.Count + "회", "fps " + _fpsLabel);
                return;
            }

            _lagSeconds.Sort();
            double mean = 0;
            for (int i = 0; i < _lagSeconds.Count; i++) mean += _lagSeconds[i];
            mean /= _lagSeconds.Count;

            double offsetRange = 0;
            if (_offsets.Count > 1)
            {
                // 오프셋은 천천히 변할 수 있으므로 앞뒤 1%를 뺀 범위를 본다.
                _offsets.Sort();
                offsetRange = Percentile(_offsets, 0.99) - Percentile(_offsets, 0.01);
            }

            bool pass = offsetRange < 0.0001;
            string measured = string.Format("탭 {0}회, 입력 지연(처리 시각 - ctx.time) 평균 {1} p50 {2} p95 {3} 최대 {4}, 매퍼 오프셋 변동 {5}",
                _lagSeconds.Count, SpikeReport.Ms(mean), SpikeReport.Ms(Percentile(_lagSeconds, 0.5)),
                SpikeReport.Ms(Percentile(_lagSeconds, 0.95)), SpikeReport.Ms(_lagSeconds[_lagSeconds.Count - 1]),
                SpikeReport.Ms(offsetRange));
            SpikeReport.Summary("SP11", SpikeReport.PassIf(pass), "매퍼 오프셋 변동 < 0.1ms(입력 지연은 기록용)", measured,
                "fps " + _fpsLabel + ", 실제 평균 fps " + SpikeReport.Num(1.0 / Time.smoothDeltaTime, "0"));
        }

        private void OnPerformed(InputAction.CallbackContext context)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            long qpc = Stopwatch.GetTimestamp();
            double lag = now - context.time;
            _lagSeconds.Add(lag);
            _csv.WriteLine(Time.frameCount + "," + SpikeReport.Num(context.time, "0.000000") + "," +
                           SpikeReport.Num(now, "0.000000") + "," + qpc + "," + SpikeReport.Num(lag * 1000.0, "0.000"));
        }

        private static double Percentile(List<double> sorted, double fraction)
        {
            int index = (int)System.Math.Round((sorted.Count - 1) * fraction);
            return sorted[index];
        }
    }
}
#endif
