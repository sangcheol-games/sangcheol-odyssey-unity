using System;
using System.Collections.Generic;
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
            public double countdownTargetTime;
            public bool isCountdownActive;
        }
        private LaneState[] _lanes;

        public ChartState()
        {
            _lanes = new LaneState[LANE_COUNT];
            for (int i = 0; i < LANE_COUNT; i++)
                _lanes[i] = new LaneState();
        }

        public void Init()
        {
            for(int i=0; i<LANE_COUNT; ++i)
            {
                _lanes[i].activeNotes.Clear();
                _lanes[i].ghostNotes.Clear();
                _lanes[i].isHolding = false;
                _lanes[i].bufferedInput = null;
                _lanes[i].isCountdownActive = false;
            }
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
                if (!_lanes[i].isCountdownActive) continue;

                double timeDiff = _lanes[i].countdownTargetTime - currentTime;

                if (timeDiff <= 0)
                {
                    onTimeDiffMinus(i);
                    _lanes[i].isCountdownActive = false;
                    continue;
                }

                onUpdateRemaining(i, timeDiff);
            }
        }

        public void SyncTime(
            double time,
            Action<NoteController> onNeedToActivate,
            double judgementOffsetSec,
            Action<NoteController, int, JudgeType> applyJudgement
        ){
            for (int i = 0; i < LANE_COUNT; i++)
            {
                CheckMissedNotes(i, time, onNeedToActivate);

                if (_lanes[i].isHolding)
                {
                    CheckNoteBody(
                        listIndex: i,
                        currentTime: time,
                        judgementOffsetSec: judgementOffsetSec,
                        // HoldEnd도 Holding과 동일하게 누르고 있는지 판정
                        acceptMask: Mask(NoteType.Holding, NoteType.HoldEnd),
                        window: JUDGE_PERFECT,
                        applyJudgement: applyJudgement,
                        judgeType: JudgeType.Perfect
                    );
                }
            }
        }

        public void CheckNoteBody(
            int listIndex,
            double currentTime,
            double judgementOffsetSec,
            int acceptMask,
            float window,
            Action<NoteController, int, JudgeType> applyJudgement,
            JudgeType? judgeType = null
        ){
            var queue = GetActiveNotes(listIndex: listIndex);
            if (queue.Count == 0) return;

            //Debug.Log($"Lane {listIndex+1} Holding now, currentTime: {currentTime}");

            NoteController note = queue.Peek();
            if(!Accepts(acceptMask, note.noteData.noteType)) return;

            // 판정 타이밍 오프셋 적용: 윈도우 중심을 noteTime + offsetSec으로 이동
            double timeDiff = Math.Abs(currentTime - note.noteData.time - judgementOffsetSec);

            // 판정 범위 밖
            if (timeDiff > window)
            {
                //Debug.Log("판정 범위 밖 입력");
                return;
            }

            //Debug.Log($"Note Judged: {type}");
            note.OnHit();
            DequeueActiveNotes(listIndex: listIndex);
            applyJudgement(note, listIndex, judgeType ?? GetJudgeType(timeDiff));
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

        public void CheckActivateCountdown(int index, double targetTime, Action<int> onNeedToActivate)
        {
            if (_lanes[index].isCountdownActive && Math.Abs(_lanes[index].countdownTargetTime - targetTime) < 0.01d) return;

            onNeedToActivate(index);

            _lanes[index].countdownTargetTime = targetTime;
            _lanes[index].isCountdownActive = true;
        }

        /// <summary>
        /// 맨 앞 노트가 Umm 윈도우(+JUDGE_UMM)까지 지나도록 판정되지 않았으면 miss 처리(Umm).
        /// SyncTime에서 레인마다 매 프레임 호출.
        /// </summary>
        private void CheckMissedNotes(int listIndex, double currentTime, Action<NoteController> onNoteMissed)
        {
            if (_lanes[listIndex].activeNotes.Count == 0) return;

            NoteController targetNote = _lanes[listIndex].activeNotes.Peek();

            if (currentTime > targetNote.noteData.time + JUDGE_UMM)
            {
                _lanes[listIndex].activeNotes.Dequeue();
                targetNote.OnMiss();

                onNoteMissed(targetNote);
            }
        }

        public void SetLaneHolding(int listIndex, bool value)
        {
            _lanes[listIndex].isHolding = value;
        }

        public Queue<NoteController> GetActiveNotes(int listIndex)
        {
            return _lanes[listIndex].activeNotes;
        }

        public void EnqueueActiveNotes(int listIndex, NoteController note)
        {
            _lanes[listIndex].activeNotes.Enqueue(note);
        }

        public NoteController DequeueActiveNotes(int listIndex)
        {
            return _lanes[listIndex].activeNotes.Dequeue();
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
        public void FlushBufferedInput(
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

                    EnqueueActiveNotes(i, note);
                    FlushBufferedInput(
                        listIndex: i,
                        onFlush: (inputTime) => tryJudgeInput(i + 1, inputTime)
                    );
                }
            }
        }
    }
}
