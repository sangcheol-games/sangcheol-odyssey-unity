using System.Collections.Generic;


namespace SCOdyssey.Game
{
    public class ChartData
    {
        public int bpm;
        public int totalNotes;
        private List<LaneData> chart;

        // TODO: 4/4박자가 아닐경우의 BarDuration 계산 (BPM 기반)
        public double BarDuration => 60f / bpm * 4f; // 4/4박자 기준

        public ChartData()
        {
            chart = new List<LaneData>();
        }

        public void AddLane(LaneData laneData)
        {
            chart.Add(laneData);
        }

        public List<LaneData> GetFullChartList()
        {
            return chart;
        }
    }
}
