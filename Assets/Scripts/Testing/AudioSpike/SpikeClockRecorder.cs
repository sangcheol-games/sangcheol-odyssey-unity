#if SCO_AUDIO_HARNESS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SCOdyssey.Testing.AudioSpike
{
    // SP3: DSP 클록과 QPC를 나란히 기록해 클록 계단 폭, 드리프트, 정지 간격을 잰다.
    // 원본 CSV(q0, q1, clock)를 남기므로 요약이 부족하면 나중에 다시 분석할 수 있다.
    public sealed class SpikeClockRecorder
    {
        // 두 QPC 읽기 사이가 이보다 길면 읽는 도중 스레드가 밀린 것이므로 버린다.
        private const double MaxReadSeconds = 50e-6;

        private static readonly double QpcToSeconds = 1.0 / Stopwatch.Frequency;

        private SpikeFmodSystem _system;
        private StreamWriter _csv;
        private long _startQpc;
        private double _durationSeconds;
        private bool _gcStress;

        private bool _hasLast;
        private ulong _lastClock;
        private double _lastEdgeSeconds;
        private ulong _firstClock;
        private int _samples;
        private int _rejected;
        private double _maxGapSeconds;
        private double _maxSampleGapSeconds;
        private double _lastSampleSeconds;
        private string _burstResult = "";
        private readonly Dictionary<ulong, int> _stepCounts = new Dictionary<ulong, int>();
        private readonly List<double> _edgeQpcSeconds = new List<double>();
        private readonly List<double> _edgeClockSeconds = new List<double>();
        private readonly List<double> _frameQpcSeconds = new List<double>();
        private readonly List<ulong> _frameClocks = new List<ulong>();

        public bool IsRecording { get; private set; }

        public double ElapsedSeconds
        {
            get { return (Stopwatch.GetTimestamp() - _startQpc) * QpcToSeconds; }
        }

        public void Begin(SpikeFmodSystem system, double durationSeconds, bool gcStress)
        {
            _system = system;
            _durationSeconds = durationSeconds;
            _gcStress = gcStress;
            _hasLast = false;
            _samples = 0;
            _rejected = 0;
            _maxGapSeconds = 0;
            _maxSampleGapSeconds = 0;
            _lastSampleSeconds = 0;
            _stepCounts.Clear();
            _edgeQpcSeconds.Clear();
            _edgeClockSeconds.Clear();
            _frameQpcSeconds.Clear();
            _frameClocks.Clear();
            _burstResult = MeasureBurst(2.0);
            _startQpc = Stopwatch.GetTimestamp();
            _csv = SpikeReport.OpenCsv("sp3_clock_" + system.Output + "_" + system.BufferLength + "x" + system.BufferCount, "q0_ticks,q1_ticks,dsp_clock");
            IsRecording = true;
        }

        // Update와 LateUpdate에서 한 번씩 부른다. 기록 시간이 끝나면 true를 돌려준다.
        public bool Sample(float deltaTime)
        {
            if (!IsRecording) return false;

            if (_gcStress)
            {
                // 초당 약 50MB의 쓰레기를 만들어 GC가 오디오 경로에 주는 영향을 본다.
                int bytes = (int)(50 * 1024 * 1024 * deltaTime);
                if (bytes > 0)
                {
                    byte[] garbage = new byte[bytes];
                    garbage[0] = 1;
                }
            }

            long q0 = Stopwatch.GetTimestamp();
            ulong clock = _system.ReadMasterClock();
            long q1 = Stopwatch.GetTimestamp();
            _samples++;

            if ((q1 - q0) * QpcToSeconds > MaxReadSeconds)
            {
                _rejected++;
                return CheckDone();
            }

            _csv.Write(q0);
            _csv.Write(',');
            _csv.Write(q1);
            _csv.Write(',');
            _csv.WriteLine(clock);

            double midSeconds = ((q0 + q1) * 0.5 - _startQpc) * QpcToSeconds;
            double sampleGap = midSeconds - _lastSampleSeconds;
            if (_samples > 1 && sampleGap > _maxSampleGapSeconds) _maxSampleGapSeconds = sampleGap;
            _lastSampleSeconds = midSeconds;
            _frameQpcSeconds.Add(midSeconds);
            _frameClocks.Add(clock);

            if (!_hasLast)
            {
                _hasLast = true;
                _lastClock = clock;
                _firstClock = clock;
                _lastEdgeSeconds = midSeconds;
                return CheckDone();
            }

            if (clock != _lastClock)
            {
                ulong step = clock - _lastClock;
                int count;
                _stepCounts.TryGetValue(step, out count);
                _stepCounts[step] = count + 1;

                double gap = midSeconds - _lastEdgeSeconds;
                if (gap > _maxGapSeconds) _maxGapSeconds = gap;

                _edgeQpcSeconds.Add(midSeconds);
                _edgeClockSeconds.Add((double)(clock - _firstClock) / _system.MixerRate);
                _lastClock = clock;
                _lastEdgeSeconds = midSeconds;
            }

            return CheckDone();
        }

        public void Finish()
        {
            if (!IsRecording) return;
            IsRecording = false;
            _csv.Flush();
            _csv.Dispose();
            _csv = null;

            double blockSeconds = _system.BlockSeconds;
            string steps = DescribeSteps();

            if (_edgeQpcSeconds.Count < 10)
            {
                SpikeReport.Summary("SP3", SpikeReport.Result.Fail, "클록 변화 10회 이상", "변화 " + _edgeQpcSeconds.Count + "회", _system.Describe());
                return;
            }

            // 최소제곱 직선 clock = a + b·qpc. 기울기 b에서 드리프트(ppm)를 얻는다.
            FitLine(_edgeQpcSeconds, _edgeClockSeconds, out double intercept, out double slope);
            double driftPpm = (slope - 1.0) * 1e6;

            // 가장자리 관측은 항상 클록이 오른 뒤에 일어나므로 잔차는 한쪽으로 치우친다. 분포의 폭을 본다.
            var residuals = new List<double>(_edgeQpcSeconds.Count);
            for (int i = 0; i < _edgeQpcSeconds.Count; i++)
            {
                double predicted = intercept + slope * _edgeQpcSeconds[i];
                residuals.Add(_edgeClockSeconds[i] - predicted);
            }
            residuals.Sort();
            double spread = Percentile(residuals, 0.99) - Percentile(residuals, 0.01);

            // 믹서가 OS 주기마다 여러 블록을 몰아 믹스하면 원시 잔차 폭은 1블록을 넘는다.
            // 판정은 설계의 곡 시계 모델을 적용한 프레임 오차로 한다.
            string model = SimulateSongClock(out double jitterP99);
            bool pass = jitterP99 <= 0.001;
            // 클록 변화 간격이 프레임 간격보다 훨씬 길면 믹서가 실제로 멈춘 것이다. 둘을 나란히 보여 준다.
            string measured = string.Format("{0} | {1} | 프레임 기록: 길이 {2}s, 샘플 {3}(버림 {4}), 평균 샘플 간격 {5}, 최대 샘플 간격(메인 스레드 멈춤) {6}, 클록 변화 {7}회, 프레임 사이 계단 {8}, 드리프트 {9}ppm, 원시 잔차 폭(p1~p99) {10}, 블록 {11}, 클록 변화 최대 간격 {12}, GC 부하 {13}",
                _burstResult, model, SpikeReport.Num(ElapsedSeconds, "0"), _samples, _rejected,
                SpikeReport.Ms(ElapsedSeconds / _samples), SpikeReport.Ms(_maxSampleGapSeconds),
                _edgeQpcSeconds.Count, steps, SpikeReport.Num(driftPpm, "0.0"), SpikeReport.Ms(spread),
                SpikeReport.Ms(blockSeconds), SpikeReport.Ms(_maxGapSeconds), _gcStress);
            SpikeReport.Summary("SP3", SpikeReport.PassIf(pass), "곡 시계 모델 프레임 오차 p99 ≤ 1ms", measured, _system.Describe());
        }

        // 설계의 곡 시계 모델: 최근 1초의 하한 포락선(c - q·R의 최댓값)으로 연속 DSP를 추정하고
        // c_read ≤ DspAt ≤ c_read + S_max로 클램프한다. 프레임 사이 곡 시각 증가가 QPC 증가와 얼마나 다른지 잰다.
        private string SimulateSongClock(out double jitterP99)
        {
            const double WindowSeconds = 1.0;
            double rate = _system.MixerRate;
            double sMax = _system.BufferLength * (_system.BufferCount + 1.0);
            int count = _frameQpcSeconds.Count;

            var offsets = new double[count];
            for (int i = 0; i < count; i++) offsets[i] = _frameClocks[i] - _frameQpcSeconds[i] * rate;

            // 구간 최댓값을 단조 덱으로 구한다.
            var window = new int[count];
            int head = 0;
            int tail = 0;
            var jitters = new List<double>(count);
            int clampHigh = 0;
            int backward = 0;
            double maxLead = 0;
            double previous = 0;
            bool hasPrevious = false;

            for (int i = 0; i < count; i++)
            {
                while (tail > head && offsets[window[tail - 1]] <= offsets[i]) tail--;
                window[tail++] = i;
                while (_frameQpcSeconds[window[head]] < _frameQpcSeconds[i] - WindowSeconds) head++;

                double raw = _frameClocks[i];
                double estimate = _frameQpcSeconds[i] * rate + offsets[window[head]];
                double lead = (estimate - raw) / rate;
                if (lead > maxLead) maxLead = lead;
                if (estimate < raw) estimate = raw;
                if (estimate > raw + sMax)
                {
                    estimate = raw + sMax;
                    clampHigh++;
                }

                // 포락선이 채워지는 첫 1초는 평가하지 않는다.
                if (_frameQpcSeconds[i] < WindowSeconds) continue;
                if (hasPrevious)
                {
                    if (estimate < previous) backward++;
                    double songStep = (estimate - previous) / rate;
                    double qpcStep = _frameQpcSeconds[i] - _frameQpcSeconds[i - 1];
                    jitters.Add(Math.Abs(songStep - qpcStep));
                }
                previous = estimate;
                hasPrevious = true;
            }

            if (jitters.Count < 10)
            {
                jitterP99 = double.MaxValue;
                return "곡 시계 모델: 샘플 부족";
            }
            jitters.Sort();
            jitterP99 = Percentile(jitters, 0.99);
            return string.Format("곡 시계 모델: 프레임 오차 p99 {0} 최대 {1}, 역행 {2}회, 상한 클램프 {3}회(S_max {4}), 원시 대비 최대 앞섬 {5}",
                SpikeReport.Ms(jitterP99), SpikeReport.Ms(jitters[jitters.Count - 1]), backward, clampHigh,
                SpikeReport.Ms(sMax / rate), SpikeReport.Ms(maxLead));
        }

        // 메인 스레드에서 seconds초 동안 클록을 쉬지 않고 읽어, fps와 관계없이
        // 클록이 한 번에 오르는 폭(샘플)과 갱신 간격을 잰다. 그동안 화면은 멈춘다.
        private string MeasureBurst(double seconds)
        {
            var steps = new Dictionary<ulong, int>();
            var intervals = new List<double>();
            long start = Stopwatch.GetTimestamp();
            long end = start + (long)(seconds * Stopwatch.Frequency);
            ulong last = _system.ReadMasterClock();
            long lastChange = start;
            bool seenChange = false;

            while (true)
            {
                long now = Stopwatch.GetTimestamp();
                if (now >= end) break;
                ulong clock = _system.ReadMasterClock();
                if (clock == last) continue;

                ulong step = clock - last;
                int count;
                steps.TryGetValue(step, out count);
                steps[step] = count + 1;
                if (seenChange) intervals.Add((now - lastChange) * QpcToSeconds);
                seenChange = true;
                last = clock;
                lastChange = now;
            }

            if (intervals.Count < 2) return "연속 읽기: 클록 변화 부족(" + intervals.Count + "회)";

            intervals.Sort();
            var parts = new List<string>();
            foreach (var pair in steps.OrderByDescending(pair => pair.Value).Take(3))
            {
                double ms = (double)pair.Key / _system.MixerRate * 1000.0;
                parts.Add(pair.Key + "(" + SpikeReport.Num(ms, "0.00") + "ms) x" + pair.Value);
            }
            return string.Format("연속 읽기 {0}s: 계단 {1}, 갱신 간격 p50 {2} p95 {3} 최대 {4}",
                SpikeReport.Num(seconds, "0"), string.Join(" / ", parts),
                SpikeReport.Ms(Percentile(intervals, 0.5)), SpikeReport.Ms(Percentile(intervals, 0.95)),
                SpikeReport.Ms(intervals[intervals.Count - 1]));
        }

        private bool CheckDone()
        {
            return ElapsedSeconds >= _durationSeconds;
        }

        // 가장 자주 나온 계단 폭 3개를 "샘플 수(ms) x횟수" 형식으로 보여 준다.
        private string DescribeSteps()
        {
            var top = _stepCounts.OrderByDescending(pair => pair.Value).Take(3);
            var parts = new List<string>();
            foreach (var pair in top)
            {
                double ms = (double)pair.Key / _system.MixerRate * 1000.0;
                parts.Add(pair.Key + "(" + SpikeReport.Num(ms, "0.00") + "ms) x" + pair.Value);
            }
            return string.Join(" / ", parts);
        }

        private static void FitLine(List<double> xs, List<double> ys, out double intercept, out double slope)
        {
            double meanX = 0;
            double meanY = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                meanX += xs[i];
                meanY += ys[i];
            }
            meanX /= xs.Count;
            meanY /= xs.Count;

            double sxx = 0;
            double sxy = 0;
            for (int i = 0; i < xs.Count; i++)
            {
                double dx = xs[i] - meanX;
                sxx += dx * dx;
                sxy += dx * (ys[i] - meanY);
            }

            slope = 1.0;
            if (sxx > 0) slope = sxy / sxx;
            intercept = meanY - slope * meanX;
        }

        private static double Percentile(List<double> sorted, double fraction)
        {
            int index = (int)Math.Round((sorted.Count - 1) * fraction);
            return sorted[index];
        }
    }
}
#endif
