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
            public readonly Queue<NoteController> activeNotes = new();
            public readonly Queue<NoteController> ghostNotes = new();
            public bool IsAnyNotesRemain => activeNotes.Count > 0 || ghostNotes.Count > 0;

            public bool isHolding = false;
            private double? bufferedInput = null;
            public double BufferedInput
            {
                set{ bufferedInput = value; }
            }
            private double? countdownTargetTime = null;
            public double CountdownTargetTime
            {
                set{ countdownTargetTime = value; }
            }

            public void Reset()
            {
                activeNotes.Clear();
                ghostNotes.Clear();
                isHolding = false;
                bufferedInput = null;
                countdownTargetTime = null;
            }

            public double? TakeFlushBufferedInput()
            {
                var result = bufferedInput;
                bufferedInput = null;
                return result;
            }

            public double? TakeCountdownTargetTime()
            {
                var result = countdownTargetTime;
                countdownTargetTime = null;
                return result;
            }

            public NoteController TryDequeueActiveNotes(
                Predicate<NoteController> shouldDequeue
            ){
                var queue = activeNotes;

                if(queue.Count == 0) return null;
                if(!shouldDequeue(queue.Peek())) return null;

                return queue.Dequeue();
            }
        }

        private class LaneList: IEnumerable<(Lane, LaneState)>
        {
            private readonly LaneState[] lanes = new LaneState[LANE_COUNT]
            {
                new(),
                new(),
                new(),
                new()
            };

            public LaneState this[Lane lane]
            {
                get => lanes[(int)lane];
            }

            public int Count => lanes.Length;

            public IEnumerator<(Lane, LaneState)> GetEnumerator()
            {
                for (int i = 0; i < lanes.Length; i++)
                    yield return ((Lane)i, lanes[i]);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                => GetEnumerator();

            public void Reset()
            {
                for(int i=0; i<LANE_COUNT; ++i)
                {
                    lanes[i].Reset();
                }
            }

            public bool IsAnyNotesRemain()
            {
                for (int i = 0; i < LANE_COUNT; i++)
                {
                    if (lanes[i].IsAnyNotesRemain) return true;
                }

                return false;
            }
        };

        private readonly LaneList lanes = new();
        private double _judgementOffsetSec;   // 유저 설정 판정 오프셋(초). 판정 윈도우 중심을 이동시킴

        public void Init(double judgementOffsetSec)
        {
            lanes.Reset();

            _judgementOffsetSec = judgementOffsetSec;
        }

        public bool IsGameClear() => lanes.IsAnyNotesRemain();

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

        public void SyncTime(
            double time,
            Action<NoteController> onNeedToActivate,
            Action<NoteController, Lane, JudgeType> applyJudgement
        ){
            foreach(var (_, state) in lanes)
            {
                var note = state.TryDequeueActiveNotes(
                    shouldDequeue: (note) => IsPastWindow(note, time, JUDGE_UMM)
                );

                if(note != null)
                {
                    note.OnMiss();
                    onNeedToActivate(note);
                }
            }

            foreach(var (lane, state) in lanes)
            {
                if(!state.isHolding) continue;

                var note = state.TryDequeueActiveNotes(
                    shouldDequeue: (note) =>
                    {
                        var typeMatched = note.AnyOf(NoteType.Holding, NoteType.HoldEnd);
                        var insideWindow = IsWithinWindow(note, time, JUDGE_PERFECT); 

                        return typeMatched && insideWindow;
                    }
                );

                if(note != null)
                {
                    //Debug.Log($"Note Judged: {type}");
                    note.OnHit();
                    applyJudgement(note, lane, JudgeType.Perfect);
                }
            }
        }

        public NoteController TryDequeueActiveNotes(
            Lane lane,
            Predicate<NoteController> shouldDequeue
        )
        {
            var queue = lanes[lane].activeNotes;

            if(queue.Count == 0) return null;
            if(!shouldDequeue(queue.Peek())) return null;

            return queue.Dequeue();
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

        public void SetLaneHolding(Lane lane, bool value)
        {
            lanes[lane].isHolding = value;
        }

        public Queue<NoteController> GetActiveNotes(Lane lane)
        {
            return lanes[lane].activeNotes;
        }

        public void EnqueueGhostNotes(Lane lane, NoteController note)
        {
            lanes[lane].ghostNotes.Enqueue(note);
        }

        public void SetBufferedInput(Lane lane, double inputGameTime)
        {
            lanes[lane].BufferedInput = inputGameTime;
        }

        /// <summary>
        /// 선입력 버퍼를 소비하여 TryJudgeInput을 재호출.
        /// press → release → barStart 케이스: isLaneHolding이 false이면 버퍼 폐기 (phantom 홀딩 방지).
        /// </summary>
        private void FlushBufferedInput(
            Lane lane,
            Action<double> onFlush
        ){
            var bufferedInput = lanes[lane].TakeFlushBufferedInput();
            if (!bufferedInput.HasValue) return;
            if (!lanes[lane].isHolding) return; // 이미 손을 뗀 경우 폐기

            onFlush((double)bufferedInput);
        }

        // 레인 인덱스(0~3) → 그룹 ID. 0~1 = 그룹0(상단), 2~3 = 그룹1(하단)
        private int GetTrackGroupID(int laneIndex)
        {
            return laneIndex <= 1 ? 0 : 1;
        }

        /// <summary>
        /// 마디 시작 시, 모든 레인의 ghostNotes를 Active로 올려 activeNotes(판정 대상)로 이동시킨다.
        /// HoldStart는 홀드바 fill 애니메이션을 위해 판정선 추적을 연결하고, 선입력 버퍼가 있으면 flush한다.
        /// </summary>
        public void ActivateGhostNotes(
            Dictionary<LaneGroup, TimelineController> activeTimelines,
            Action<int, double> tryJudgeInput
        ){
            foreach(var (lane, state) in lanes)
            {
                while (state.ghostNotes.Count > 0)
                {
                    NoteController note = state.ghostNotes.Dequeue();
                    note.SetState(NoteState.Active);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    // Holding/HoldEnd는 비주얼 없으므로 추적 불필요
                    if (note.noteData.noteType == NoteType.HoldStart)
                    {
                        var groupID = LaneExtensions.GetGroup(lane);
                        if (activeTimelines.TryGetValue(groupID, out var timeline))
                        {
                            note.TrackTimeline(timeline);
                        }
                    }

                    state.activeNotes.Enqueue(note);
                    FlushBufferedInput(
                        lane: lane,
                        onFlush: (inputTime) => tryJudgeInput((int)lane + 1, inputTime)
                    );
                }
            }
        }
    }
}
