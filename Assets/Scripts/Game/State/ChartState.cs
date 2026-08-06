using System;
using System.Collections.Generic;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public enum LaneGroup
    {
        Top,
        Bottom,
    };

    public enum Lane
    {
        TopUpper = 0,
        TopLower = 1,
        BottomUpper = 2,
        BottomLower = 3,
    };

    public static class LaneExtensions
    {
        public static LaneGroup GetGroup(this Lane lane)
            => (int)lane < 2 ? LaneGroup.Top : LaneGroup.Bottom;
    };

    public class ChartState{
        private class LaneState
        {
            private readonly Queue<NoteController> activeNotes = new();
            internal readonly Queue<NoteController> ghostNotes = new();
            internal bool IsActiveNotesRemain => activeNotes.Count > 0;
            internal bool IsAnyNotesRemain => IsActiveNotesRemain || ghostNotes.Count > 0;

            internal bool isHolding = false;
            private double? bufferedInput = null;
            internal double BufferedInput
            {
                set{ bufferedInput = value; }
            }
            private double? countdownTargetTime = null;
            internal double CountdownTargetTime
            {
                set{ countdownTargetTime = value; }
            }

            internal void Reset()
            {
                activeNotes.Clear();
                ghostNotes.Clear();
                isHolding = false;
                bufferedInput = null;
                countdownTargetTime = null;
            }

            internal double? TakeFlushBufferedInput()
            {
                var result = bufferedInput;
                bufferedInput = null;
                return result;
            }

            internal double? TakeCountdownTargetTime()
            {
                var result = countdownTargetTime;
                countdownTargetTime = null;
                return result;
            }

            internal bool TryDequeueActiveNotes(
                Predicate<NoteController> shouldDequeue,
                out NoteController note
            ){
                var queue = activeNotes;

                if(queue.Count == 0 || !shouldDequeue(queue.Peek()))
                {
                    note = default;
                    return false;
                }

                note = queue.Dequeue();
                return true;
            }

            internal void ActivateGhostNotes(TimelineController timeline)
            {
                while (ghostNotes.Count > 0)
                {
                    NoteController note = ghostNotes.Dequeue();
                    note.SetState(NoteState.Active);

                    activeNotes.Enqueue(note);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    // Holding/HoldEnd는 비주얼 없으므로 추적 불필요
                    if (note.noteData.noteType == NoteType.HoldStart)
                    {
                        if (timeline != null)
                        {
                            note.TrackTimeline(timeline);
                        }
                    }
                }
            }
        }

        // Helper class for Enumerating LaneStates
        private class LaneList: IEnumerable<(Lane, LaneState)>
        {
            private readonly LaneState[] lanes = new LaneState[LANE_COUNT]
            {
                new(),
                new(),
                new(),
                new()
            };

            internal LaneState this[Lane lane]
            {
                get => lanes[(int)lane];
            }

            internal int Count => lanes.Length;

            public IEnumerator<(Lane, LaneState)> GetEnumerator()
            {
                for (int i = 0; i < lanes.Length; i++)
                    yield return ((Lane)i, lanes[i]);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                => GetEnumerator();
        };

        private readonly LaneList lanes = new();
        private double _judgementOffsetSec;   // 유저 설정 판정 오프셋(초). 판정 윈도우 중심을 이동시킴

        public void Init(double judgementOffsetSec)
        {
            foreach(var (_, state) in lanes)
            {
                state.Reset();
            }

            _judgementOffsetSec = judgementOffsetSec;
        }

        public bool IsGameClear()
        {
            foreach(var (_, state) in lanes)
            {
                if (state.IsAnyNotesRemain) return true;
            }

            return false;
        }

        public void UpdateCountdowns(
            double currentTime,
            Action<Lane> onTimeDiffMinus,
            Action<Lane, double> onUpdateRemaining
        ){
            foreach(var (lane, state) in lanes)
            {
                var countdownTargetTime = state.TakeCountdownTargetTime();
                if(!countdownTargetTime.HasValue) continue;

                double timeDiff = countdownTargetTime.Value - currentTime;
                if(timeDiff < 0)
                {
                    onTimeDiffMinus(lane);
                }
                else
                {
                    onUpdateRemaining(lane, timeDiff);
                }
            }
        }

        public double ToNoteLocalTime(NoteController note, double currentTime)
            => note.noteData.time + _judgementOffsetSec - currentTime;

        public bool IsWithinWindow(
            NoteController note,
            double currentTime,
            float window
        )
        {
            double timeDiff = Math.Abs(ToNoteLocalTime(note, currentTime));
            return timeDiff < window;
        }

        private bool IsPastWindow(
            NoteController note,
            double currentTime,
            float window
        )
        {
            return ToNoteLocalTime(note, currentTime) < -window;
        }

        public void CheckNoteMissed(
            double time,
            Action<NoteController> onNoteMissed
        )
        {
            foreach(var (_, state) in lanes)
            {
                if(state.TryDequeueActiveNotes(
                    shouldDequeue: (note) => IsPastWindow(note, time, JUDGE_UMM),
                    out var note
                ))
                {
                    note.OnMiss();
                    onNoteMissed(note);
                }
            }
        }

        public void CheckNoteHolding(
            double time,
            Action<NoteController, Lane, JudgeType> applyJudgement
        )
        {
            foreach(var (lane, state) in lanes)
            {
                if(!state.isHolding) continue;

                if(state.TryDequeueActiveNotes(
                    shouldDequeue: (note) =>
                    {
                        var typeMatched = note.AnyOf(NoteType.Holding, NoteType.HoldEnd);
                        var insideWindow = IsWithinWindow(note, time, JUDGE_PERFECT); 

                        return typeMatched && insideWindow;
                    },
                    out var note
                ))
                {
                    //Debug.Log($"Note Judged: {type}");
                    note.OnHit();
                    applyJudgement(note, lane, JudgeType.Perfect);
                }
            }
        }

        public bool TryJudgeInput(
            Lane lane,
            double inputGameTime,
            out NoteController judgedNote,
            out JudgeType judgeResult
        )
        {
            var state = lanes[lane];

            state.isHolding = true;

            if (!state.IsActiveNotesRemain)
            {
                // 마디 전환 직전 선입력: 노트가 활성화되면 FlushBufferedInput에서 재판정
                state.BufferedInput = inputGameTime;

                judgedNote = default;
                judgeResult = default;
                return false;
            }

            double timeDiff = 0.0;
            if(state.TryDequeueActiveNotes(
                shouldDequeue: (note) =>
                {
                    var typeMatched = note.AnyOf(NoteType.Normal, NoteType.HoldStart);
                    timeDiff = Math.Abs(ToNoteLocalTime(note, inputGameTime));
                    var insideWindow = timeDiff < JUDGE_UMM;

                    return typeMatched && insideWindow;
                },
                out judgedNote
            ))
            {
                judgeResult = GetJudgeType(timeDiff);
                return true;
            }

            judgedNote = default;
            judgeResult = default;
            return false;
        }

        public bool TryJudgeRelease(
            Lane lane,
            double inputGameTime,
            out NoteController judgedNote,
            out JudgeType judgeResult
        )
        {
            var state = lanes[lane];

            state.isHolding = false;

            double timeDiff = 0.0;
            if(state.TryDequeueActiveNotes(
                shouldDequeue: (note) =>
                {
                    var typeMatched = note.AnyOf(NoteType.HoldRelease);
                    timeDiff = Math.Abs(ToNoteLocalTime(note, inputGameTime));
                    var insideWindow = timeDiff < JUDGE_UMM;

                    return typeMatched && insideWindow;
                },
                out judgedNote
            ))
            {
                judgeResult = GetJudgeType(timeDiff);
                return true;
            }

            judgedNote = default;
            judgeResult = default;
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

        public void ActivateCountdown(Lane lane, double targetTime)
        {
            lanes[lane].CountdownTargetTime = targetTime;
        }

        public void EnqueueGhostNotes(Lane lane, NoteController note)
        {
            lanes[lane].ghostNotes.Enqueue(note);
        }

        /// <summary>
        /// 마디 시작 시, 모든 레인의 ghostNotes를 Active로 올려 activeNotes(판정 대상)로 이동시킨다.
        /// HoldStart는 홀드바 fill 애니메이션을 위해 판정선 추적을 연결하고, 선입력 버퍼가 있으면 flush한다.
        /// </summary>
        public void ActivateGhostNotes(
            Dictionary<LaneGroup, TimelineController> activeTimelines
        ){
            foreach(var (lane, state) in lanes)
            {
                var groupID = LaneExtensions.GetGroup(lane);

                if(activeTimelines.TryGetValue(
                    key: groupID,
                    out var timeline
                ))
                {
                    state.ActivateGhostNotes(timeline);
                }
            }
        }

        public void ConsumeBufferedInput(
            Action<NoteController, Lane, JudgeType> applyJudgement
        )
        {
            foreach(var (lane, state) in lanes)
            {
                // 선입력 버퍼를 소비하여 TryJudgeInput을 재호출.
                // press → release → barStart 케이스: isLaneHolding이 false이면 버퍼 폐기 (phantom 홀딩 방지).
                var bufferedInput = state.TakeFlushBufferedInput();

                if(!bufferedInput.HasValue || !state.isHolding) continue;

                if(TryJudgeInput(
                    lane: lane,
                    (double) bufferedInput,
                    out var judgedNote,
                    out var judgeResult
                ))
                {
                    judgedNote.OnHit();
                    applyJudgement(judgedNote, lane, judgeResult);
                }
            }
        }
    }
}
