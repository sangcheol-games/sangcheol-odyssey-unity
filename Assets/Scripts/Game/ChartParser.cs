using System;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 채보 텍스트 → ChartData 변환기. 여기서 마디/노트의 모든 시간을 미리 계산해 넣는다.
    // (런타임에는 시간을 다시 계산하지 않고 이 값을 그대로 판정에 쓴다.)
    public static class ChartParser
    {

        /// <summary>
        /// 채보 텍스트를 파싱해 ChartData를 만든다(GameDataLoader가 호출).
        /// 헤더(#KEY value)와 데이터(#마디:채널레인:시퀀스;)를 구분해 처리하고,
        /// 마디 시작 시각·노트 판정 시각을 모두 계산해 채운다.
        /// </summary>
        public static ChartData Parse(string chartText, int bpm)
        {
            ChartData chartData = new ChartData();
            chartData.bpm = bpm;

            // 1. 줄 단위로 나누기 (윈도우/맥/리눅스 개행문자 대응)
            string[] lines = chartText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM
            double duration = (60f / bpm) * 4f;  // TODO: 박자표(4/4)가 아닐때 가변적으로 처리 필요

            foreach (string line in lines)
            {
                if (!line.StartsWith("#")) continue;

                // 헤더 라인 파싱 (#KEY value)
                if (!line.Contains(':') || !line.EndsWith(";"))
                {
                    ParseHeaderField(line, chartData);
                    continue;
                }

                try
                {
                    // 데이터 파싱: #001:02:01020020; -> 001, 02, 01020020
                    string content = line.TrimStart('#').TrimEnd(';');
                    string[] parts = content.Split(':');

                    if (parts.Length < 3) continue;

                    // 마디 정보
                    int barNumber = int.Parse(parts[0]);

                    // 채널 및 레인 정보
                    string channelLaneStr = parts[1];
                    int channel = channelLaneStr[0] - '0'; // char -> int 변환
                    int lane = channelLaneStr[1] - '0';

                    bool isLTR = (channel == 0);

                    // 노트 데이터 정보
                    string noteSequence = parts[2];
                    int beat = noteSequence.Length;

                    // 마디 시작 시간 계산: 마디번호 * 마디당 시간
                    double laneStartTime = barNumber * duration;

                    // LaneData 생성
                    LaneData laneData = new LaneData(barNumber, laneStartTime, beat, isLTR, lane);

                    // 개별 노트 생성
                    laneData.ConvertSequenceToNotes(noteSequence, duration);

                    // 파싱된 레인 데이터를 차트에 추가
                    chartData.AddLane(laneData);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Chart Parsing Error at line: {line}\n{e.Message}");
                }
            }

            Debug.Log($"Chart Parsed Successfully. Total Lanes: {chartData.GetFullChartList().Count}, Total Notes: {chartData.totalNotes}");
            return chartData;
        }

        private static void ParseHeaderField(string line, ChartData chartData)
        {
            string content = line.TrimStart('#').Trim();
            int spaceIndex = content.IndexOf(' ');
            if (spaceIndex < 0) return;

            string key = content[..spaceIndex].ToUpper();
            string value = content[(spaceIndex + 1)..].Trim();

            switch (key)
            {
                case "NOTES":
                    if (int.TryParse(value, out int notes))
                        chartData.totalNotes = notes;
                    break;
            }
        }
    }
}