using System;
using System.Collections.Generic;

namespace SCOdyssey.Rhythm
{
    // 입력 스크립트를 앞에서부터 소비하며 now까지의 입력을 엔진에 넣는다.
    public sealed class ScriptCursor
    {
        private readonly IReadOnlyList<ScriptedInput> _inputs;
        private int _next;

        public ScriptCursor(InputScript script)
        {
            _inputs = script.Inputs;
        }

        public bool IsDone => _next >= _inputs.Count;

        public void Feed(JudgeEngine engine, double now, List<JudgeEvent> outEvents, List<ScriptedInput> fed = null)
        {
            while (_next < _inputs.Count && _inputs[_next].Time <= now)
            {
                ScriptedInput input = _inputs[_next++];
                if (input.IsPress) engine.Press(input.Lane, input.Time, outEvents);
                else engine.Release(input.Lane, input.Time, outEvents);
                fed?.Add(input);
            }
        }
    }

    // 프레임마다 "그 시각까지의 입력 -> Advance" 순서로 엔진을 몬다(게임 루프와 같은 순서).
    public static class ScriptedRun
    {
        public static List<JudgeEvent> Run(JudgeNote[] track, JudgeSettings settings, InputScript script, IEnumerable<double> frames)
        {
            var engine = new JudgeEngine(settings);
            engine.Load(track);
            var events = new List<JudgeEvent>();
            Run(engine, script, frames, events);
            return events;
        }

        public static void Run(JudgeEngine engine, InputScript script, IEnumerable<double> frames, List<JudgeEvent> outEvents)
        {
            var cursor = new ScriptCursor(script);
            foreach (double now in frames)
            {
                cursor.Feed(engine, now, outEvents);
                engine.Advance(now, outEvents);
            }
        }
    }

    // 프레임 시각 목록
    public static class FrameSchedule
    {
        public static IEnumerable<double> Uniform(double stepSec, double endSec, double startSec = 0)
        {
            for (int k = 0; ; k++)
            {
                double t = Math.Min(startSec + k * stepSec, endSec);
                yield return t;
                if (t >= endSec) yield break;
            }
        }

        // hitchAt 이후 첫 프레임이 직전 프레임보다 hitchSec 늦게 온다. 그 사이 입력은 그 프레임에 한꺼번에 처리된다
        public static IEnumerable<double> WithHitch(double stepSec, double endSec, double hitchAt, double hitchSec)
        {
            double last = double.NaN;
            foreach (double t in Uniform(stepSec, endSec))
            {
                if (t > hitchAt) break;
                last = t;
                yield return t;
            }

            if (double.IsNaN(last) || last >= endSec) yield break;

            foreach (double t in Uniform(stepSec, endSec, last + hitchSec))
                yield return t;
        }
    }

    // 시계가 흐른 만큼 고정 간격 판정 프레임을 하나씩 내준다. 화면 프레임레이트와 무관하게 FrameSchedule과 같은 프레임이 나온다.
    public sealed class FrameStepper
    {
        private readonly double _stepSec;
        private double _hitchAt = double.NaN;
        private double _hitchSec;

        public double Next { get; private set; }

        public FrameStepper(double startSec, double stepSec)
        {
            Next = startSec;
            _stepSec = stepSec;
        }

        // at 이후 첫 프레임이 직전 프레임보다 sec 늦게 온다(FrameSchedule.WithHitch와 같은 규칙)
        public void ScheduleHitch(double at, double sec)
        {
            _hitchAt = at;
            _hitchSec = sec;
        }

        // 지금 멈춘 것처럼 다음 프레임을 lastFrame + sec 뒤로 미룬다
        public void DelayNext(double lastFrame, double sec) => Next = Math.Max(Next, lastFrame + sec);

        public bool TryTake(double upTo, out double frame)
        {
            frame = Next;
            if (Next > upTo) return false;

            bool hitch = !double.IsNaN(_hitchAt) && frame <= _hitchAt && frame + _stepSec > _hitchAt;
            Next = frame + (hitch ? _hitchSec : _stepSec);
            if (hitch) _hitchAt = double.NaN;
            return true;
        }
    }
}
