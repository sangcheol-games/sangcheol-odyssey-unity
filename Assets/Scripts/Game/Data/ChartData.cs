using System.Collections.Generic;


namespace SCOdyssey.Game
{
    public class ChartData
    {
        public int bpm;
        private List<LaneData> chart;

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
