using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 시각 순 입력 목록. 같은 시각끼리는 넣은 순서를 지킨다.
    // 텍스트 형식: 한 줄에 "시각(초) 레인(1~4) P|R", '#'부터 줄 끝까지는 주석
    public sealed class InputScript
    {
        private List<ScriptedInput> _inputs = new();
        private bool _sorted = true;

        public int Count => _inputs.Count;

        public IReadOnlyList<ScriptedInput> Inputs
        {
            get
            {
                if (!_sorted)
                {
                    _inputs = _inputs.OrderBy(i => i.Time).ToList();   // OrderBy는 안정 정렬
                    _sorted = true;
                }
                return _inputs;
            }
        }

        public InputScript Add(ScriptedInput input)
        {
            if (_inputs.Count > 0 && input.Time < _inputs[^1].Time) _sorted = false;
            _inputs.Add(input);
            return this;
        }

        public InputScript Press(Lane lane, double time) => Add(ScriptedInput.Press(time, lane));
        public InputScript Release(Lane lane, double time) => Add(ScriptedInput.Release(time, lane));
        public InputScript Tap(Lane lane, double time, double holdSec = 0.03) => Press(lane, time).Release(lane, time + holdSec);
        public InputScript Hold(Lane lane, double from, double to) => Press(lane, from).Release(lane, to);

        public InputScript Shifted(double sec)
        {
            var shifted = new InputScript();
            foreach (ScriptedInput input in Inputs)
                shifted.Add(new ScriptedInput(input.Time + sec, input.Lane, input.IsPress));
            return shifted;
        }

        public double LastTime => Count == 0 ? double.NegativeInfinity : Inputs[^1].Time;

        public static InputScript Parse(string text, List<string> errors = null)
        {
            var script = new InputScript();
            string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int comment = line.IndexOf('#');
                if (comment >= 0) line = line[..comment];
                line = line.Trim();
                if (line.Length == 0) continue;

                if (TryParseLine(line, out ScriptedInput input)) script.Add(input);
                else errors?.Add($"{n + 1}번째 줄을 읽을 수 없다: \"{lines[n]}\" (형식: 시각 레인(1~4) P|R)");
            }

            return script;
        }

        private static bool TryParseLine(string line, out ScriptedInput input)
        {
            input = default;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int laneNumber)) return false;
            if (laneNumber < LaneMap.FIRST_LANE || laneNumber >= LaneMap.FIRST_LANE + LANE_COUNT) return false;

            bool isPress;
            switch (parts[2].ToUpperInvariant())
            {
                case "P": isPress = true; break;
                case "R": isPress = false; break;
                default: return false;
            }

            input = new ScriptedInput(time, LaneMap.FromInputIndex(laneNumber), isPress);
            return true;
        }

        public override string ToString() => string.Join("\n", Inputs.Select(i => i.ToString()));
    }
}
