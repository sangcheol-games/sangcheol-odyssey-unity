using System.Collections.Generic;


namespace SCOdyssey.Game
{
    // 파싱이 끝난 한 곡·난이도의 채보 전체. ChartManager.Init이 GetFullChartList()로 받아 remainingChart에 적재한다.
    // 개별 노트의 판정 시각은 파싱 단계(ChartParser/LaneData)에서 이미 계산되어 들어 있다.
    public class ChartData
    {
        public int bpm;                    // 곡 BPM. barDuration 계산 근거
        public int totalNotes;             // 총 노트 수(헤더 #NOTES). ScoreManager 기본점수 계산에 사용
        private List<LaneData> chart;      // 마디×레인 단위 LaneData 목록(파싱 순서 = 마디 순서)

        public ChartData()
        {
            chart = new List<LaneData>();
        }

        public void AddLane(LaneData laneData)
        {
            chart.Add(laneData);
        }

        // 전체 LaneData 목록 반환. ChartManager가 이 목록으로 Queue<LaneData> remainingChart를 만든다.
        public List<LaneData> GetFullChartList()
        {
            return chart;
        }
    }
}
