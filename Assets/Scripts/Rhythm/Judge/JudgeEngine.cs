using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Rhythm
{
    // 판정의 단일 권한. 시간 오름차순 배열 + 상태 배열 + 커서만으로 판정한다. Unity 참조 없음.
    //
    // 커서는 "Umm 윈도우가 닫힌 지점"만 따라가고, 지나갈 때 Pending인 노트를 Missed로 뱉는다.
    // 판정 순서와 커서 진행 순서가 다르므로(인덱스 5가 hit인데 3이 Pending일 수 있음)
    // 커서 하나로는 표현이 안 되고 상태 배열이 따로 필요하다.
    public sealed class JudgeEngine : IJudgeStateReader
    {
        private static readonly int PRESS_KINDS = Mask(NoteType.Normal, NoteType.HoldStart);
        private static readonly int RELEASE_KINDS = Mask(NoteType.HoldRelease);
        private static readonly int HOLD_BODY_KINDS = Mask(NoteType.Holding, NoteType.HoldEnd);

        private readonly JudgeSettings _settings;
        private JudgeNote[] _notes = Array.Empty<JudgeNote>();
        private NoteStatus[] _status = Array.Empty<NoteStatus>();
        private JudgeType[] _grade = Array.Empty<JudgeType>();
        private int _missCursor;
        private readonly bool[] _isHolding = new bool[LANE_COUNT];

        public JudgeEngine(JudgeSettings settings)
        {
            _settings = settings;
        }

        public JudgeSettings Settings => _settings;
        public double Now { get; private set; } = double.NegativeInfinity;
        public int Count => _notes.Length;
        public bool IsFinished => _missCursor >= _notes.Length;

        private JudgeWindows Windows => _settings.Windows;

        public void Load(JudgeNote[] notes)
        {
            _notes = notes ?? Array.Empty<JudgeNote>();
            Reset();
        }

        public void Reset()
        {
            _status = new NoteStatus[_notes.Length];
            _grade = new JudgeType[_notes.Length];
            _missCursor = 0;
            Array.Clear(_isHolding, 0, _isHolding.Length);
            Now = double.NegativeInfinity;
        }

        public NoteStatus StatusOf(int noteId) => _status[noteId];
        public JudgeType? GradeOf(int noteId) => _status[noteId] == NoteStatus.Judged ? _grade[noteId] : null;
        public JudgeNote NoteAt(int noteId) => _notes[noteId];
        public bool IsHeld(Lane lane) => _isHolding[(int)lane];

        private double TimeOf(int index) => _notes[index].Time + _settings.OffsetSec;

        /// <summary>
        /// 매 프레임 호출. 시각은 되돌아가지 않는다(Now보다 이른 time은 Now로 본다).
        /// 윈도우를 지나친 노트를 miss로 확정하고, 누르고 있는 레인의 홀드 본체(Holding/HoldEnd)를 Perfect로 자동 판정한다.
        /// </summary>
        public void Advance(double time, List<JudgeEvent> outEvents)
        {
            if (time > Now) Now = time;
            double now = Now;

            // 1) 커서 전진. Umm 윈도우가 닫혔는데 아직 Pending이면 miss
            while (_missCursor < _notes.Length && TimeOf(_missCursor) - now < -Windows.Umm)
            {
                if (_status[_missCursor] == NoteStatus.Pending)
                {
                    _status[_missCursor] = NoteStatus.Missed;
                    double closedAt = TimeOf(_missCursor) + Windows.Umm;
                    outEvents.Add(JudgeEvent.Miss(_missCursor, _notes[_missCursor], Windows.Umm, closedAt));
                }
                _missCursor++;
            }

            // 2) 홀드 본체는 누르고 있기만 하면 Perfect
            for (int i = _missCursor; i < _notes.Length; i++)
            {
                double diff = TimeOf(i) - now;
                if (diff >= Windows.Perfect) break;   // 시간 오름차순이라 뒤는 더 멀다

                if (_status[i] != NoteStatus.Pending) continue;

                JudgeNote note = _notes[i];
                if (!_isHolding[(int)note.Lane]) continue;
                if (!Accepts(HOLD_BODY_KINDS, note.Kind)) continue;
                if (-diff >= Windows.Perfect) continue;   // 이미 지나침. miss 커서가 처리한다

                MarkJudged(i, JudgeType.Perfect);
                outEvents.Add(new JudgeEvent(i, note.Lane, note.Kind, JudgeType.Perfect, false, -diff, now));
            }
        }

        public bool Press(Lane lane, double time, List<JudgeEvent> outEvents)
        {
            _isHolding[(int)lane] = true;
            return TryClaim(lane, time, PRESS_KINDS, outEvents);
        }

        public bool Release(Lane lane, double time, List<JudgeEvent> outEvents)
        {
            _isHolding[(int)lane] = false;
            return TryClaim(lane, time, RELEASE_KINDS, outEvents);
        }

        // 커서부터 앞으로 훑어 윈도우 안의 Pending 노트 하나를 집는다(어느 것인지는 Select 정책).
        // 마디 경계와 무관하게 윈도우만 보므로 선입력 버퍼가 필요 없다.
        private bool TryClaim(Lane lane, double time, int kindMask, List<JudgeEvent> outEvents)
        {
            int picked = -1;
            double pickedDiff = 0;

            for (int i = _missCursor; i < _notes.Length; i++)
            {
                double diff = TimeOf(i) - time;
                if (diff >= Windows.Umm) break;      // 아직 윈도우에 안 들어온 미래

                if (_status[i] != NoteStatus.Pending) continue;

                JudgeNote note = _notes[i];
                if (note.Lane != lane) continue;
                if (-diff >= Windows.Umm) continue;  // 윈도우를 이미 지남
                if (!Accepts(kindMask, note.Kind)) continue;

                if (picked < 0 || Math.Abs(diff) < Math.Abs(pickedDiff))
                {
                    picked = i;
                    pickedDiff = diff;
                }

                if (_settings.Select == NoteSelectPolicy.Earliest) break;
            }

            if (picked < 0) return false;

            JudgeType grade = Windows.Grade(Math.Abs(pickedDiff));
            MarkJudged(picked, grade);
            outEvents.Add(new JudgeEvent(picked, lane, _notes[picked].Kind, grade, false, -pickedDiff, time));
            return true;
        }

        private void MarkJudged(int index, JudgeType grade)
        {
            _status[index] = NoteStatus.Judged;
            _grade[index] = grade;
        }
    }
}
