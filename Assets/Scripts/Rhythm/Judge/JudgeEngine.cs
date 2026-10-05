using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정의 단일 권한. 시간 오름차순 배열 + 상태 배열 + 커서만으로 판정한다. Unity 참조 없음.
    //
    // 탭·홀드 머리는 누르는 타이밍, 홀드 꼬리는 떼는 타이밍으로 판정한다.
    // 머리가 판정되면 꼬리는 InProgress(누르는 중)가 되고, 꼬리 윈도우 안에서 떼면 판정, 그 전에 떼면 즉시 Miss(홀드 끊김),
    // 윈도우가 지나도록 누르고 있으면 Miss. 머리를 놓치면 꼬리도 같이 Miss.
    //
    // 모든 입력은 먼저 그 시각까지 Advance한 뒤 처리하므로 결과가 프레임 간격과 무관하다.
    // 커서는 "윈도우가 닫힌 지점"만 따라가며 Pending 노트를 Missed로 뱉는다.
    public sealed class JudgeEngine : IJudgeStateReader
    {
        private readonly JudgeSettings _settings;
        private JudgeNote[] _notes = Array.Empty<JudgeNote>();
        private NoteStatus[] _status = Array.Empty<NoteStatus>();
        private JudgeType[] _grade = Array.Empty<JudgeType>();
        private readonly int[] _holding = new int[LANE_COUNT];     // 레인별 InProgress 꼬리. 없으면 -1
        private readonly bool[] _keyDown = new bool[LANE_COUNT];
        private int _cursor;
        private int _decided;

        public JudgeEngine(JudgeSettings settings)
        {
            _settings = settings;
            Array.Fill(_holding, -1);
        }

        public JudgeSettings Settings => _settings;
        public double Now { get; private set; } = double.NegativeInfinity;
        public int Count => _notes.Length;
        public bool IsFinished => _decided >= _notes.Length;

        private JudgeWindows Windows => _settings.Windows;

        // 트랙은 복사하지 않고 그대로 쓴다. 커서 스윕과 홀드 짝이 기대는 전제(시간순, 짝이 서로를 가리킴)만 검사한다
        public void Load(JudgeNote[] notes)
        {
            notes ??= Array.Empty<JudgeNote>();
            Validate(notes);
            _notes = notes;
            Reset();
        }

        private static void Validate(JudgeNote[] notes)
        {
            for (int i = 0; i < notes.Length; i++)
            {
                JudgeNote note = notes[i];
                if (i > 0 && notes[i - 1].Time > note.Time)
                    throw new ArgumentException($"판정 트랙이 시간순이 아니다: #{i - 1} {notes[i - 1]} 다음 #{i} {note}");

                if (note.PairId < 0) continue;
                if (note.PairId >= notes.Length || note.PairId == i)
                    throw new ArgumentException($"#{i} {note}의 짝 인덱스가 범위 밖이다");

                JudgeNote pair = notes[note.PairId];
                bool headToTail = note.Kind == NoteKind.HoldHead && pair.Kind == NoteKind.HoldTail && note.PairId > i;
                bool tailToHead = note.Kind == NoteKind.HoldTail && pair.Kind == NoteKind.HoldHead && note.PairId < i;
                if (pair.PairId != i || pair.Lane != note.Lane || !(headToTail || tailToHead))
                    throw new ArgumentException($"홀드 짝이 맞지 않는다: #{i} {note} ↔ #{note.PairId} {pair}");
            }
        }

        public void Reset()
        {
            _status = new NoteStatus[_notes.Length];
            _grade = new JudgeType[_notes.Length];
            Array.Fill(_holding, -1);
            Array.Clear(_keyDown, 0, _keyDown.Length);
            _cursor = 0;
            _decided = 0;
            Now = double.NegativeInfinity;
        }

        public NoteStatus StatusOf(int noteId) => _status[noteId];
        public JudgeType? GradeOf(int noteId) => _status[noteId] == NoteStatus.Judged ? _grade[noteId] : null;
        public JudgeNote NoteAt(int noteId) => _notes[noteId];
        public bool IsHeld(Lane lane) => _keyDown[(int)lane];
        public int HoldInProgressOf(Lane lane) => _holding[(int)lane];

        private double TimeOf(int index) => _notes[index].Time + _settings.OffsetSec;
        private double WindowOf(int index) => _notes[index].Kind == NoteKind.HoldTail ? _settings.TailWindow : Windows.Umm;
        private double ClosedAt(int index) => TimeOf(index) + WindowOf(index);   // 이 시각을 넘으면(>) 윈도우가 닫힌 것

        /// <summary>
        /// 시각을 time까지 진행한다. 되돌아가지 않는다(Now보다 이른 time은 Now로 본다).
        /// 누르고 있는 홀드의 꼬리 윈도우가 지났으면 Miss, 윈도우가 닫힌 Pending 노트도 Miss로 확정한다.
        /// </summary>
        public void Advance(double time, List<JudgeEvent> outEvents)
        {
            if (time > Now) Now = time;
            double now = Now;
            int first = outEvents.Count;

            for (int lane = 0; lane < LANE_COUNT; lane++)
            {
                int tail = _holding[lane];
                if (tail < 0) continue;

                double closedAt = ClosedAt(tail);
                if (now <= closedAt) continue;

                _holding[lane] = -1;
                Decide(tail, NoteStatus.Missed, JudgeType.Umm);
                outEvents.Add(JudgeEvent.Miss(tail, _notes[tail], _settings.TailWindow, closedAt));
            }

            while (_cursor < _notes.Length && now > ClosedAt(_cursor))
            {
                if (_status[_cursor] == NoteStatus.Pending) SweepMiss(_cursor, outEvents);
                _cursor++;
            }

            SortByTime(outEvents, first);
        }

        // 홀드 시간 초과를 레인별로 먼저 처리하므로 한 번에 나온 miss가 시각순이 아닐 수 있다.
        // 받는 쪽이 순서를 믿을 수 있게 이번에 추가한 구간만 Time 순으로 맞춘다(같은 시각이면 원래 순서 유지)
        private static void SortByTime(List<JudgeEvent> events, int first)
        {
            for (int i = first + 1; i < events.Count; i++)
            {
                JudgeEvent e = events[i];
                int j = i - 1;
                while (j >= first && events[j].Time > e.Time)
                {
                    events[j + 1] = events[j];
                    j--;
                }
                events[j + 1] = e;
            }
        }

        public bool Press(Lane lane, double time, List<JudgeEvent> outEvents)
        {
            Advance(Math.Max(time, Now), outEvents);
            _keyDown[(int)lane] = true;

            int picked = FindPressTarget(lane, time);
            if (picked < 0) return false;

            JudgeNote note = _notes[picked];
            double delta = time - TimeOf(picked);
            JudgeType grade = Windows.Grade(Math.Abs(delta));
            Decide(picked, NoteStatus.Judged, grade);
            outEvents.Add(JudgeEvent.Hit(picked, note, grade, delta, time));

            if (note.Kind == NoteKind.HoldHead && note.PairId >= 0 && _status[note.PairId] == NoteStatus.Pending)
            {
                BreakHold(lane, time, outEvents);   // 떼지 않은 채 다음 머리를 눌렀다면 앞 홀드는 끊긴 것
                _status[note.PairId] = NoteStatus.InProgress;
                _holding[(int)lane] = note.PairId;
            }
            return true;
        }

        public bool Release(Lane lane, double time, List<JudgeEvent> outEvents)
        {
            Advance(Math.Max(time, Now), outEvents);
            _keyDown[(int)lane] = false;

            int tail = _holding[(int)lane];
            if (tail < 0) return false;
            _holding[(int)lane] = -1;

            JudgeNote note = _notes[tail];
            double delta = time - TimeOf(tail);
            if (Math.Abs(delta) < _settings.TailWindow)
            {
                JudgeType grade = Windows.Grade(Math.Abs(delta));
                Decide(tail, NoteStatus.Judged, grade);
                outEvents.Add(JudgeEvent.Hit(tail, note, grade, delta, time));
            }
            else
            {
                // 꼬리 윈도우 전에 뗐다. 다시 눌러도 복구되지 않는다
                Decide(tail, NoteStatus.Missed, JudgeType.Umm);
                outEvents.Add(JudgeEvent.Miss(tail, note, delta, time));
            }
            return true;
        }

        private void BreakHold(Lane lane, double time, List<JudgeEvent> outEvents)
        {
            int tail = _holding[(int)lane];
            if (tail < 0) return;

            _holding[(int)lane] = -1;
            Decide(tail, NoteStatus.Missed, JudgeType.Umm);
            outEvents.Add(JudgeEvent.Miss(tail, _notes[tail], time - TimeOf(tail), time));
        }

        // 윈도우가 닫힌 Pending 노트. 머리면 짝 꼬리도 같은 시각에 죽는다
        private void SweepMiss(int index, List<JudgeEvent> outEvents)
        {
            JudgeNote note = _notes[index];
            double closedAt = ClosedAt(index);
            Decide(index, NoteStatus.Missed, JudgeType.Umm);
            outEvents.Add(JudgeEvent.Miss(index, note, WindowOf(index), closedAt));

            if (note.Kind != NoteKind.HoldHead || note.PairId < 0 || _status[note.PairId] != NoteStatus.Pending) return;

            Decide(note.PairId, NoteStatus.Missed, JudgeType.Umm);
            outEvents.Add(JudgeEvent.Miss(note.PairId, _notes[note.PairId], _settings.TailWindow, closedAt));
        }

        // 커서부터 앞으로 훑어 윈도우 안의 Pending 탭·머리 하나를 고른다(어느 것인지는 Select 정책).
        // 마디 경계와 무관하게 윈도우만 보므로 선입력 버퍼가 필요 없다.
        private int FindPressTarget(Lane lane, double time)
        {
            int picked = -1;
            double pickedDiff = 0;

            for (int i = _cursor; i < _notes.Length; i++)
            {
                double diff = TimeOf(i) - time;
                if (diff >= Windows.Umm) break;      // 아직 윈도우에 안 들어온 미래

                if (_status[i] != NoteStatus.Pending) continue;

                JudgeNote note = _notes[i];
                if (note.Lane != lane || note.Kind == NoteKind.HoldTail) continue;
                if (-diff >= Windows.Umm) continue;  // 윈도우를 이미 지남

                if (picked < 0 || Math.Abs(diff) < Math.Abs(pickedDiff))
                {
                    picked = i;
                    pickedDiff = diff;
                }

                if (_settings.Select == NoteSelectPolicy.Earliest) break;
            }

            return picked;
        }

        private void Decide(int index, NoteStatus status, JudgeType grade)
        {
            _status[index] = status;
            _grade[index] = grade;
            _decided++;
        }
    }
}
