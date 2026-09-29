using System.Collections.Generic;
using SCOdyssey.Rhythm;
using UnityEngine;
using UnityEngine.UIElements;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정 상태를 직선 시간축으로 그리는 두 번째 뷰. IJudgeStateReader만 읽는다(그룹·방향·마디·Ghost 개념 없음).
    // 레인 4줄이 오른쪽에서 왼쪽으로 흐르고, 흰 세로선이 현재 시각이다.
    public sealed class JudgeTimelineElement : VisualElement
    {
        public static readonly Color PendingColor = new(0.72f, 0.72f, 0.76f);
        public static readonly Color MissColor = new(1f, 0.3f, 0.3f);
        public static readonly Color InputBandColor = new(0.35f, 1f, 0.45f, 0.85f);
        public static readonly Color HoldActiveColor = new(0.4f, 0.9f, 1f, 0.7f);

        private static readonly string[] LaneKeys = { "Q", "A", "'", "/" };
        private const float NoteWidth = 10f;
        private const int MaxQuads = 16000;   // ushort 인덱스 한도

        private readonly List<(int head, int tail)> _holdSpans = new();
        private readonly Label[] _laneLabels = new Label[LANE_COUNT];
        private readonly List<Vertex> _vertices = new();
        private readonly List<ushort> _indices = new();
        private readonly double[] _pressedAt = new double[LANE_COUNT];
        private IJudgeStateReader _reader;
        private IReadOnlyList<ScriptedInput> _inputs;

        public double PastSec { get; set; } = 1.0;
        public double FutureSec { get; set; } = 3.0;

        public JudgeTimelineElement()
        {
            AddToClassList("timeline");
            generateVisualContent += Generate;

            for (int lane = 0; lane < LANE_COUNT; lane++)
            {
                _laneLabels[lane] = new Label($"레인 {lane + 1} · {LaneKeys[lane]}") { pickingMode = PickingMode.Ignore };
                _laneLabels[lane].AddToClassList("lane-label");
                Add(_laneLabels[lane]);
            }
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLaneLabels());
        }

        public static Color GradeColor(JudgeType grade) => grade switch
        {
            JudgeType.Perfect => new Color(0.4f, 0.9f, 1f),
            JudgeType.Master => new Color(0.4f, 1f, 0.5f),
            JudgeType.Ideal => new Color(1f, 0.9f, 0.3f),
            JudgeType.Kind => new Color(1f, 0.6f, 0.2f),
            _ => new Color(0.8f, 0.45f, 1f),
        };

        public void Bind(IJudgeStateReader reader, IReadOnlyList<ScriptedInput> inputs)
        {
            _reader = reader;
            _inputs = inputs;
            _holdSpans.Clear();

            var open = new int[LANE_COUNT];
            for (int lane = 0; lane < LANE_COUNT; lane++) open[lane] = -1;

            for (int i = 0; i < reader.Count; i++)
            {
                JudgeNote note = reader.NoteAt(i);
                int lane = (int)note.Lane;
                if (note.Kind == NoteType.HoldStart)
                {
                    open[lane] = i;
                }
                else if (note.Kind == NoteType.HoldEnd || note.Kind == NoteType.HoldRelease)
                {
                    if (open[lane] >= 0) _holdSpans.Add((open[lane], i));
                    open[lane] = -1;
                }
            }
            MarkDirtyRepaint();
        }

        public void Refresh()
        {
            if (_reader == null) return;
            for (int lane = 0; lane < LANE_COUNT; lane++)
                _laneLabels[lane].EnableInClassList("lane-label--held", _reader.IsHeld((Lane)lane));
            MarkDirtyRepaint();
        }

        private void PlaceLaneLabels()
        {
            float rowHeight = contentRect.height / LANE_COUNT;
            for (int lane = 0; lane < LANE_COUNT; lane++) _laneLabels[lane].style.top = lane * rowHeight + 4;
        }

        private void Generate(MeshGenerationContext mgc)
        {
            Rect area = contentRect;
            if (_reader == null || area.width <= 0 || area.height <= 0) return;
            double now = _reader.Now;
            if (double.IsInfinity(now)) return;

            _vertices.Clear();
            _indices.Clear();
            float rowHeight = area.height / LANE_COUNT;

            for (int lane = 0; lane < LANE_COUNT; lane++)
                Quad(new Rect(area.xMin, area.yMin + lane * rowHeight, area.width, rowHeight),
                    lane % 2 == 0 ? new Color(1, 1, 1, 0.03f) : new Color(1, 1, 1, 0.07f));

            foreach ((int head, int tail) in _holdSpans) HoldBar(area, rowHeight, now, head, tail);
            for (int i = 0; i < _reader.Count; i++) Note(area, rowHeight, now, i);

            if (_inputs != null) InputBands(area, rowHeight, now);

            Quad(new Rect(X(area, now, now) - 1.5f, area.yMin, 3, area.height), Color.white);

            if (_vertices.Count == 0) return;
            MeshWriteData mesh = mgc.Allocate(_vertices.Count, _indices.Count);
            mesh.SetAllVertices(_vertices.ToArray());
            mesh.SetAllIndices(_indices.ToArray());
        }

        // 키를 누르고 있던 구간을 레인 아래쪽 띠로 그린다. 시작 = 누른 순간, 끝 = 뗀 순간, 아직 누르는 중이면 지금까지
        private void InputBands(Rect area, float rowHeight, double now)
        {
            for (int lane = 0; lane < LANE_COUNT; lane++) _pressedAt[lane] = double.NaN;

            foreach (ScriptedInput input in _inputs)
            {
                int lane = (int)input.Lane;
                if (input.IsPress)
                {
                    if (double.IsNaN(_pressedAt[lane])) _pressedAt[lane] = input.Time;
                }
                else if (!double.IsNaN(_pressedAt[lane]))
                {
                    Band(area, rowHeight, now, lane, _pressedAt[lane], input.Time);
                    _pressedAt[lane] = double.NaN;
                }
            }

            for (int lane = 0; lane < LANE_COUNT; lane++)
            {
                if (!double.IsNaN(_pressedAt[lane])) Band(area, rowHeight, now, lane, _pressedAt[lane], now);
            }
        }

        private void Band(Rect area, float rowHeight, double now, int lane, double from, double to)
        {
            float rawX0 = X(area, now, from);
            float rawX1 = X(area, now, to);
            if (rawX1 < area.xMin || rawX0 > area.xMax) return;

            float x0 = Mathf.Max(area.xMin, rawX0);
            float x1 = Mathf.Min(area.xMax, Mathf.Max(rawX1, rawX0 + 3));   // 아주 짧은 탭도 보이게
            Rect row = Row(area, lane, rowHeight);
            float height = Mathf.Max(4f, row.height * 0.09f);
            Quad(new Rect(x0, row.yMax - row.height * 0.04f - height, x1 - x0, height), InputBandColor);
        }

        private void HoldBar(Rect area, float rowHeight, double now, int head, int tail)
        {
            JudgeNote h = _reader.NoteAt(head);
            JudgeNote t = _reader.NoteAt(tail);
            float x0 = Mathf.Max(area.xMin, X(area, now, h.Time));
            float x1 = Mathf.Min(area.xMax, X(area, now, t.Time));
            if (x1 <= x0) return;

            Rect row = Row(area, (int)h.Lane, rowHeight);
            Color color = _reader.StatusOf(tail) switch
            {
                NoteStatus.Missed => new Color(MissColor.r, MissColor.g, MissColor.b, 0.3f),
                NoteStatus.Judged => WithAlpha(GradeColor(_reader.GradeOf(tail) ?? JudgeType.Umm), 0.35f),
                _ => _reader.StatusOf(head) == NoteStatus.Judged && _reader.IsHeld(h.Lane)
                    ? HoldActiveColor
                    : new Color(0.72f, 0.72f, 0.76f, 0.3f),
            };
            Quad(new Rect(x0, row.center.y - row.height * 0.14f, x1 - x0, row.height * 0.28f), color);
        }

        private void Note(Rect area, float rowHeight, double now, int id)
        {
            JudgeNote note = _reader.NoteAt(id);
            float x = X(area, now, note.Time);
            if (x - NoteWidth / 2 < area.xMin || x + NoteWidth / 2 > area.xMax) return;

            Rect row = Row(area, (int)note.Lane, rowHeight);
            float scale = note.Kind switch
            {
                NoteType.Holding => 0.3f,
                NoteType.HoldEnd => 0.45f,
                _ => 0.7f,
            };
            Color color = _reader.StatusOf(id) switch
            {
                NoteStatus.Judged => GradeColor(_reader.GradeOf(id) ?? JudgeType.Umm),
                NoteStatus.Missed => MissColor,
                _ => PendingColor,
            };
            Quad(new Rect(x - NoteWidth / 2, row.center.y - row.height * scale / 2, NoteWidth, row.height * scale), color);
        }

        private void Quad(Rect r, Color color)
        {
            if (_vertices.Count / 4 >= MaxQuads) return;
            var start = (ushort)_vertices.Count;
            Color32 tint = color;
            _vertices.Add(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = tint });
            _vertices.Add(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = tint });
            _vertices.Add(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = tint });
            _vertices.Add(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = tint });
            _indices.Add(start);
            _indices.Add((ushort)(start + 1));
            _indices.Add((ushort)(start + 2));
            _indices.Add((ushort)(start + 2));
            _indices.Add((ushort)(start + 3));
            _indices.Add(start);
        }

        private float X(Rect area, double now, double time)
            => area.xMin + (float)((time - (now - PastSec)) / (PastSec + FutureSec)) * area.width;

        private static Rect Row(Rect area, int lane, float rowHeight)
            => new(area.xMin, area.yMin + lane * rowHeight, area.width, rowHeight);

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
