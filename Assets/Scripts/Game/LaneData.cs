using System.Collections.Generic;

namespace SCOdyssey.Game
{
    public class LaneData
    {
        public int bar;     // 몇 번째 마디인지
        public float time;  // 노트가 출현해야하는 시간. BPM과 마디에 기반해 계산
        public int beat;    // 몇 비트인지
        public bool isCW;   // 정방향인지 역방향인지(채보파일에서 채널에 대응). 정방향(Clock-Wise)이라면 true
        public int line;    // 몇 번째 라인인지

        public Queue<NoteData> Notes;
    }
}

/*

#001:02:01020020;


//시작부호 : #뒤의 한 줄이 한 라인의 노트들에 대응한다.

//마디 : 몇 번째 마디인지를 나타낸다. 항상 3자리 수로 나타낸다.

//채널 : 특수효과를 적용하기 위해 사용한다. 0이 기본. 1일 경우 판정선이 역방향 진행한다.

//라인 : 라인의 넘버를 나타낸다.

노트 : 노트 정보를 나타낸다. 0은 노트가 없는 공백, 1은 기본노트, 2는 롱노트를 의미하며, 자릿수는 그 라인의 비트수를 나타낸다. 예시의 경우 8자리이므로 8비트

//마침부호 : ;로 한 줄을 종료한다.
*/