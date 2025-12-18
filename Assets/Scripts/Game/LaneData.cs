using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class LaneData
    {
        public int bar;     // 몇 번째 마디인지
        public float time;  // 노트가 출현해야하는 시간. BPM과 마디에 기반해 계산
        public int beat;    // 몇 비트인지
        public bool isLTR;   // 레인의 진행방향(채보파일에서 채널에 대응). Left To Right라면 true
        public int line;    // 몇 번째 라인인지

        public Queue<NoteData> Notes;

        public LaneData(int bar, float time, int beat, bool isLTR, int line)
        {
            this.bar = bar;
            this.time = time;
            this.beat = beat;
            this.isLTR = isLTR;
            this.line = line;
            Notes = new Queue<NoteData>();
        }

        public void ConvertSequenceToNotes(string noteSequence, float duration)
        {
            float stepTime = duration / beat; // 한 노트당 지속 시간

            if (!isLTR) noteSequence = new string(noteSequence.Reverse().ToArray());

            for (int i = 0; i < beat; i++)
            {
                char noteChar = noteSequence[i];
                NoteType noteType = GetNoteType(int.Parse(noteChar.ToString()));
                if (noteType == NoteType.None) continue;

                float noteTime = time + (i * stepTime);
                Notes.Enqueue(new NoteData(i, noteTime, noteType, line));
            }

        }

        private NoteType GetNoteType(int num)
        {
            switch (num)
            {
                case 1: return NoteType.Normal;
                case 2: return NoteType.Hold;
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