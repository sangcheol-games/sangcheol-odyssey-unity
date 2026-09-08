using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정의 단일 권한. 시간 오름차순 배열 + 상태 배열 + 커서만으로 판정한다. Unity 참조 없음.
    //
    // 커서는 "Umm 윈도우가 닫힌 지점"만 따라가고, 지나갈 때 Pending인 노트를 Missed로 뱉는다.
    // 판정 순서와 커서 진행 순서가 다르므로(인덱스 5가 hit인데 3이 Pending일 수 있음)
    // 커서 하나로는 표현이 안 되고 상태 배열이 따로 필요하다.
    public sealed class JudgeTrack
    {
        private static readonly int PRESS_KINDS = Mask(NoteType.Normal, NoteType.HoldStart);
        private static readonly int RELEASE_KINDS = Mask(NoteType.HoldRelease);
        private static readonly int HOLD_BODY_KINDS = Mask(NoteType.Holding, NoteType.HoldEnd);

        private JudgeNote[] _notes = Array.Empty<JudgeNote>();
        private NoteStatus[] _status = Array.Empty<NoteStatus>();
        private int _missCursor;
        private readonly bool[] _isHolding = new bool[LANE_COUNT];
        private double _judgementOffsetSec;   // 유저 설정 판정 오프셋(초). 윈도우 중심을 이동시킴

        public int Count => _notes.Length;
        public bool IsFinished => _missCursor >= _notes.Length;

        public void Init(JudgeNote[] notes, double judgementOffsetSec)
        {
            _notes = notes ?? Array.Empty<JudgeNote>();
            _status = new NoteStatus[_notes.Length];
            _missCursor = 0;
            Array.Clear(_isHolding, 0, _isHolding.Length);
            _judgementOffsetSec = judgementOffsetSec;
        }

        private double TimeOf(int index) => _notes[index].Time + _judgementOffsetSec;

        /// <summary>
        /// 매 프레임 호출. 윈도우를 지나친 노트를 miss로 확정하고,
        /// 누르고 있는 레인의 홀드 본체(Holding/HoldEnd)를 Perfect로 자동 판정한다.
        /// </summary>
        public void Tick(double time, List<JudgeEvent> outEvents)
        {
            // 1) 커서 전진. Umm 윈도우가 닫혔는데 아직 Pending이면 miss
            while (_missCursor < _notes.Length && TimeOf(_missCursor) - time < -JUDGE_UMM)
            {
                if (_status[_missCursor] == NoteStatus.Pending)
                {
                    _status[_missCursor] = NoteStatus.Missed;
                    outEvents.Add(JudgeEvent.Miss(_missCursor, _notes[_missCursor]));
                }
                _missCursor++;
            }

            // 2) 홀드 본체는 누르고 있기만 하면 Perfect
            for (int i = _missCursor; i < _notes.Length; i++)
            {
                double diff = TimeOf(i) - time;
                if (diff >= JUDGE_PERFECT) break;   // 시간 오름차순이라 뒤는 더 멀다

                if (_status[i] != NoteStatus.Pending) continue;

                JudgeNote note = _notes[i];
                if (!_isHolding[(int)note.Lane]) continue;
                if (!Accepts(HOLD_BODY_KINDS, note.Kind)) continue;
                if (-diff >= JUDGE_PERFECT) continue;   // 이미 지나침. miss 커서가 처리한다

                _status[i] = NoteStatus.Judged;
                outEvents.Add(new JudgeEvent(i, note.Lane, note.Kind, JudgeType.Perfect, false));
            }
        }

        public bool TryPress(Lane lane, double time, out JudgeEvent judged)
        {
            _isHolding[(int)lane] = true;
            return TryClaim(lane, time, PRESS_KINDS, out judged);
        }

        public bool TryRelease(Lane lane, double time, out JudgeEvent judged)
        {
            _isHolding[(int)lane] = false;
            return TryClaim(lane, time, RELEASE_KINDS, out judged);
        }

        // 커서부터 앞으로 훑어 윈도우 안의 첫 Pending 노트를 집는다.
        // 마디 경계와 무관하게 윈도우만 보므로 선입력 버퍼가 필요 없다.
        private bool TryClaim(Lane lane, double time, int kindMask, out JudgeEvent judged)
        {
            for (int i = _missCursor; i < _notes.Length; i++)
            {
                double diff = TimeOf(i) - time;
                if (diff >= JUDGE_UMM) break;      // 아직 윈도우에 안 들어온 미래

                if (_status[i] != NoteStatus.Pending) continue;

                JudgeNote note = _notes[i];
                if (note.Lane != lane) continue;
                if (-diff >= JUDGE_UMM) continue;  // 윈도우를 이미 지남
                if (!Accepts(kindMask, note.Kind)) continue;

                _status[i] = NoteStatus.Judged;
                judged = new JudgeEvent(i, lane, note.Kind, GetJudgeType(Math.Abs(diff)), false);
                return true;
            }

            judged = default;
            return false;
        }

        // 타이밍 오차(절댓값, 초)를 판정 등급으로 매핑. 윈도우 상수는 Constants.cs
        private static JudgeType GetJudgeType(double timeDiff)
        {
            if (timeDiff <= JUDGE_PERFECT) return JudgeType.Perfect;
            if (timeDiff <= JUDGE_MASTER)  return JudgeType.Master;
            if (timeDiff <= JUDGE_IDEAL)   return JudgeType.Ideal;
            if (timeDiff <= JUDGE_KIND)    return JudgeType.Kind;
            return JudgeType.Umm;
        }
    }
}
