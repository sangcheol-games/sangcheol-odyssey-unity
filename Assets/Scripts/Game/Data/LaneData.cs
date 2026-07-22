using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 한 마디 × 한 레인의 채보 데이터. ChartManager는 이 단위로 remainingChart→nextBarLanes 파이프라인을 돌린다.
    // 채보파일 한 줄(#bar:채널레인:시퀀스;)이 LaneData 하나에 대응한다.
    public class LaneData
    {
        public int bar;     // 몇 번째 마디인지 (PrepareNextBar에서 다음 마디 판별에 사용)
        public double time;  // 이 마디(레인)의 시작 게임 상대시간. bar * barDuration 으로 파싱 시 선계산
        public int beat;    // 몇 비트인지 (= 시퀀스 자릿수. 마디를 몇 등분하는지)
        public bool isLTR;   // 레인의 진행방향(채보파일에서 채널에 대응). Left To Right라면 true
        public int line;    // 몇 번째 라인인지 (1~4. ChartManager에서 line-1로 레인/그룹 인덱싱)

        public Queue<NoteData> Notes;   // 이 레인의 노트들(판정 순서대로). 각 NoteData.time은 아래에서 선계산

        public LaneData(int bar, double time, int beat, bool isLTR, int line)
        {
            this.bar = bar;
            this.time = time;
            this.beat = beat;
            this.isLTR = isLTR;
            this.line = line;
            Notes = new Queue<NoteData>();
        }

        // 시퀀스 문자열("01020020")을 NoteData 목록으로 변환하며, 각 노트의 판정 시각을 여기서 확정(선계산)한다.
        // ChartManager는 런타임에 시간을 다시 계산하지 않고 이 값을 그대로 판정에 쓴다.
        public void ConvertSequenceToNotes(string noteSequence, double duration)
        {
            double stepTime = duration / beat; // 한 노트당 지속 시간(= 비트 1칸 시간)

            if (!isLTR) noteSequence = new string(noteSequence.Reverse().ToArray());
            // RTL 반전 후: index 0 = 첫 번째로 판정되는 노트 (원래 채보 기준 오른쪽 끝)

            for (int i = 0; i < beat; i++)
            {
                char noteChar = noteSequence[i];
                NoteType noteType = GetNoteType(noteChar - '0');
                if (noteType == NoteType.None) continue;

                // 판정 시각 = 마디 시작 시각 + (비트 인덱스 × 비트 1칸 시간)
                double noteTime = time + (i * stepTime);
                NoteData noteData = new NoteData(i, noteTime, noteType, line);

                if (noteType == NoteType.HoldStart)
                {
                    // 반전 후 순서 기준으로 앞을 탐색하여 HoldEnd(4) 또는 HoldRelease(5) 위치를 찾음

                    int? holdEnd = null;
                    for (int j = i + 1; j < beat; j++)
                    {
                        int fwd = noteSequence[j] - '0';
                        if (fwd == 4 || fwd == 5)
                        {
                            holdEnd = j - i;
                            break;
                        }
                    }
                    // 없으면 마디 끝까지 (endpoint까지)
                    noteData.holdBarBeats = holdEnd ?? (beat - i);
                }

                Notes.Enqueue(noteData);
            }

        }

        private NoteType GetNoteType(int num)
        {
            switch (num)
            {
                case 1: return NoteType.Normal;
                case 2: return NoteType.HoldStart;
                case 3: return NoteType.Holding;
                case 4: return NoteType.HoldEnd;
                case 5: return NoteType.HoldRelease;
                default: return NoteType.None;
            }
        }

        public int GetTimelineStartPosition()
        {
            int index = -1;
            if (line == 1 || line == 2)
            {
                index = 0;
            }
            else if (line == 3 || line == 4)
            {
                index = 1;
            }
            return index;
        }

    }
}