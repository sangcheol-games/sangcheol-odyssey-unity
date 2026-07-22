using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 노트 1개의 순수 데이터. ChartManager는 time으로 판정, noteType으로 판정 방식 분기,
    // index로 배치 X좌표 계산, holdBarBeats로 홀드바 길이를 정한다.
    public class NoteData
    {
        public int index;           // 채보 순서(마디 내 비트 인덱스). SpawnNextNotes의 X좌표 계산에 사용
        public double time;          // 판정 시간(게임 상대시간, 파싱 시 선계산). TryJudge/CheckMissed/CheckHoldingBody에서 읽음
        public NoteType noteType;   // 노트 타입(Normal/HoldStart/Holding/HoldEnd/HoldRelease)
        public int laneIndex;       // 라인 번호(1~4)
        public int? holdBarBeats;   // HoldStart 전용: 홀드바가 뻗어야 할 비트 수 (HoldEnd/HoldRelease 위치까지)

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
