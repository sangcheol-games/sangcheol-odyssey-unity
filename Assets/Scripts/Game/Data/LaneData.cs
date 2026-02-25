using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class LaneData
    {
        public int bar;     // 몇 번째 마디인지
        public double time;  // 노트가 출현해야하는 시간. BPM과 마디에 기반해 계산
        public int beat;    // 몇 비트인지
        public bool isLTR;   // 레인의 진행방향(채보파일에서 채널에 대응). Left To Right라면 true
        public int line;    // 몇 번째 라인인지

        public Queue<NoteData> Notes;

        public LaneData(int bar, double time, int beat, bool isLTR, int line)
        {
            this.bar = bar;
            this.time = time;
            this.beat = beat;
            this.isLTR = isLTR;
            this.line = line;
            Notes = new Queue<NoteData>();
        }

        public void ConvertSequenceToNotes(string noteSequence, double duration)
        {
            double stepTime = duration / beat; // 한 노트당 지속 시간

            if (!isLTR) noteSequence = new string(noteSequence.Reverse().ToArray());
            // RTL 반전 후: index 0 = 첫 번째로 판정되는 노트 (원래 채보 기준 오른쪽 끝)

            for (int i = 0; i < beat; i++)
            {
                char noteChar = noteSequence[i];
                NoteType noteType = GetNoteType(noteChar - '0');
                if (noteType == NoteType.None) continue;

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