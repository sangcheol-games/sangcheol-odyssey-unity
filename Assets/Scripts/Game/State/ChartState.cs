using System;
using System.Collections.Generic;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class ChartState{
        private class LaneState
        {
            public readonly Queue<NoteController> activeNotes = new();
            public readonly Queue<NoteController> ghostNotes = new();
            public bool isHolding;
            public double? bufferedInput;
            public double? countdownTargetTime;
        }
        private readonly LaneState[] _lanes;

        private double _judgementOffsetSec;   // 유저 설정 판정 오프셋(초). 판정 윈도우 중심을 이동시킴

        public ChartState()
        {
            _lanes = new LaneState[LANE_COUNT];
            for (int i = 0; i < LANE_COUNT; i++)
                _lanes[i] = new LaneState();
        }

        public void Init(double judgementOffsetSec)
        {
            for(int i=0; i<LANE_COUNT; ++i)
            {
                _lanes[i].activeNotes.Clear();
                _lanes[i].ghostNotes.Clear();
                _lanes[i].isHolding = false;
                _lanes[i].bufferedInput = null;
            }

            _judgementOffsetSec = judgementOffsetSec;
        }

        public bool IsGameClear()
        {
            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (_lanes[i].activeNotes.Count > 0) return true;
                if (_lanes[i].ghostNotes.Count > 0) return true;
            }

            return false;
        }

        public void UpdateCountdowns(
            double currentTime,
            Action<int> onTimeDiffMinus,
            Action<int, double> onUpdateRemaining
        ){
            for (int i = 0; i < LANE_COUNT; i++)
            {
                if (_lanes[i].countdownTargetTime == null) continue;

                double timeDiff = _lanes[i].countdownTargetTime.Value - currentTime;

                if (timeDiff <= 0)
                {
                    onTimeDiffMinus(i);
                    _lanes[i].countdownTargetTime = null;
                    continue;
                }

                onUpdateRemaining(i, timeDiff);
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
            Action<NoteController, int, JudgeType> applyJudgement
        ){
            for (int i = 0; i < LANE_COUNT; i++)
            {
                var note = TryDequeueActiveNotes(
                    listIndex: i,
                    shouldDequeue: (note) => IsPastWindow(note, time, JUDGE_UMM)
                );

                if(note != null)
                {
                    note.OnMiss();
                    onNeedToActivate(note);
                }

                if (_lanes[i].isHolding)
                {
                    note = TryDequeueActiveNotes(
                        listIndex: i,
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
                        applyJudgement(note, i, JudgeType.Perfect);
                    }
                }
            }
        }

        public NoteController TryDequeueActiveNotes(
            int listIndex,
            Func<NoteController, bool> shouldDequeue
        )
        {
            var queue = _lanes[listIndex].activeNotes;

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

        public void ActivateCountdown(int index, double targetTime)
        {
            _lanes[index].countdownTargetTime = targetTime;
        }

        public void SetLaneHolding(int listIndex, bool value)
        {
            _lanes[listIndex].isHolding = value;
        }

        public Queue<NoteController> GetActiveNotes(int listIndex)
        {
            return _lanes[listIndex].activeNotes;
        }

        public void EnqueueGhostNotes(int index, NoteController note)
        {
            _lanes[index].ghostNotes.Enqueue(note);
        }

        public void SetBufferedInput(int listIndex, double inputGameTime)
        {
            _lanes[listIndex].bufferedInput = inputGameTime;
        }

        /// <summary>
        /// 선입력 버퍼를 소비하여 TryJudgeInput을 재호출.
        /// press → release → barStart 케이스: isLaneHolding이 false이면 버퍼 폐기 (phantom 홀딩 방지).
        /// </summary>
        private void FlushBufferedInput(
            int listIndex,
            Action<double> onFlush
        ){
            if (!_lanes[listIndex].bufferedInput.HasValue) return;

            double inputTime = _lanes[listIndex].bufferedInput.Value;
            _lanes[listIndex].bufferedInput = null;

            if (!_lanes[listIndex].isHolding) return; // 이미 손을 뗀 경우 폐기

            onFlush(inputTime);
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
            Dictionary<int, TimelineController> activeTimelines,
            Action<int, double> tryJudgeInput
        ){
            for (int i = 0; i < LANE_COUNT; i++)
            {
                while (_lanes[i].ghostNotes.Count > 0)
                {
                    NoteController note = _lanes[i].ghostNotes.Dequeue();
                    note.SetState(NoteState.Active);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    // Holding/HoldEnd는 비주얼 없으므로 추적 불필요
                    if (note.noteData.noteType == NoteType.HoldStart)
                    {
                        int groupID = GetTrackGroupID(i);
                        if (activeTimelines.TryGetValue(groupID, out var timeline))
                        {
                            note.TrackTimeline(timeline);
                        }
                    }

                    _lanes[i].activeNotes.Enqueue(note);
                    FlushBufferedInput(
                        listIndex: i,
                        onFlush: (inputTime) => tryJudgeInput(i + 1, inputTime)
                    );
                }
            }
        }
    }
}
