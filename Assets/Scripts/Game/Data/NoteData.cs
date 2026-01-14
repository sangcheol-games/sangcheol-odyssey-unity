using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public class NoteData
    {
        public int index;           // 채보 순서
        public double time;          // 판정 시간
        public NoteType noteType;   // 노트 타입
        public int laneIndex;     // 라인 번호
        
        public NoteData(int index, double time, NoteType noteType, int laneIndex)
        {
            this.index = index;
            this.time = time;
            this.noteType = noteType;
            this.laneIndex = laneIndex;

            //Debug.Log($"Note Created - Index: {index}, Time: {time}, Type: {noteType}, Lane: {laneIndex}");
        }
    }
}
