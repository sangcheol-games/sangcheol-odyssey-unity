using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 판정에 딸리지 않는 뷰 상태만 들고 있다. 판정 권한은 JudgeTrack에 있다.
    //  - ghostNotes: 다음 마디용으로 미리 스폰해 둔 노트. 마디 시작 때 Active로 승격(표시 전환)
    //  - countdownTargets: 3/2/1 텍스트의 목표 시각. 레인이 아니라 슬롯(그룹 × 진행방향) 단위
    public class ChartState
    {
        private readonly Queue<NoteController>[] ghostNotes = new Queue<NoteController>[LANE_COUNT];
        private readonly double?[] countdownTargets = new double?[COUNTDOWN_SLOT_COUNT];

        public ChartState()
        {
            for (int i = 0; i < ghostNotes.Length; i++) ghostNotes[i] = new Queue<NoteController>();
        }

        public void Init()
        {
            foreach (var queue in ghostNotes) queue.Clear();
            Array.Clear(countdownTargets, 0, countdownTargets.Length);
        }

        public void ActivateCountdown(CountdownSlot slot, double targetTime)
        {
            countdownTargets[(int)slot] = targetTime;
        }

        public void UpdateCountdowns(
            double currentTime,
            Action<CountdownSlot> onTimeDiffMinus,
            Action<CountdownSlot, double> onUpdateRemaining
        ){
            for(int i = 0; i < countdownTargets.Length; i++)
            {
                if(!countdownTargets[i].HasValue) continue;

                double timeDiff = countdownTargets[i].Value - currentTime;
                if(timeDiff <= 0)
                {
                    onTimeDiffMinus((CountdownSlot)i);
                    countdownTargets[i] = null;
                }
                else
                {
                    onUpdateRemaining((CountdownSlot)i, timeDiff);
                }
            }
        }

        public void EnqueueGhostNotes(Lane lane, NoteController note)
        {
            ghostNotes[(int)lane].Enqueue(note);
        }

        /// <summary>
        /// 마디 시작 시 모든 레인의 ghostNotes를 Active로 올린다(표시만 바뀐다).
        /// HoldStart는 홀드바 fill 애니메이션을 위해 판정선 추적을 연결한다.
        /// </summary>
        public void ActivateGhostNotes(
            Dictionary<LaneGroup, TimelineController> activeTimelines
        ){
            for(int i = 0; i < ghostNotes.Length; i++)
            {
                var group = ((Lane)i).GetGroup();
                activeTimelines.TryGetValue(group, out var timeline);

                var queue = ghostNotes[i];
                while (queue.Count > 0)
                {
                    NoteController note = queue.Dequeue();
                    note.SetState(NoteState.Active);

                    // HoldStart만 타임라인 추적: 홀드바 fill 애니메이션에 사용
                    if (note.noteData.noteType == NoteType.HoldStart && timeline != null)
                    {
                        note.TrackTimeline(timeline);
                    }
                }
            }
        }
    }
}
