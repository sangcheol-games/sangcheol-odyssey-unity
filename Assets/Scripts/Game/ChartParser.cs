using System;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    public static class ChartParser
    {

        public static ChartData Parse(string chartText, int bpm)
        {
            ChartData chartData = new ChartData();
            chartData.bpm = bpm;

            // 1. 줄 단위로 나누기 (윈도우/맥/리눅스 개행문자 대응)
            string[] lines = chartText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // 마디별 진행시간 = 악보상의 박자표(4/4) * 4 * 60 / BPM
            float duration = (60f / bpm) * 4f;  // TODO: 박자표(4/4)가 아닐때 가변적으로 처리 필요

            foreach (string line in lines)
            {
                if (!line.StartsWith("#") || !line.EndsWith(";")) continue;

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
                    float laneStartTime = barNumber * duration;

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

            Debug.Log($"Chart Parsed Successfully. Total Lanes: {chartData.GetFullChartList().Count}");
            return chartData;
        }
    }
}