using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.ChartEditor.Data
{
    /// <summary>
    /// 에디터용 전체 채보 데이터. 랜덤 액세스 + 수정 가능.
    /// </summary>
    public class EditorChartData
    {
        // 헤더 메타데이터
        public string title = "";
        public string artist = "";
        public Difficulty difficulty = Difficulty.Normal;
        public int level = 1;
        public int bpm = 120;

        public AudioClip audioClip;     // 음원 파일
        public string filePath;         // 현재 저장 경로 (null = 미저장)

        // barNumber → EditorBarData
        private Dictionary<int, EditorBarData> bars = new Dictionary<int, EditorBarData>();

        public EditorChartData()
        {
            // 0번 마디는 항상 존재 (준비 마디)
            bars[0] = new EditorBarData(0);
        }

        /// <summary>
        /// 해당 마디 데이터를 반환. 없으면 새로 생성하여 반환
        /// </summary>
        public EditorBarData GetOrCreateBar(int barNumber)
        {
            if (!bars.ContainsKey(barNumber))
            {
                bars[barNumber] = new EditorBarData(barNumber);
            }
            return bars[barNumber];
        }

        /// <summary>
        /// 해당 마디 데이터가 존재하는지 확인
        /// </summary>
        public bool HasBar(int barNumber)
        {
            return bars.ContainsKey(barNumber);
        }

        /// <summary>
        /// 해당 마디 데이터 반환 (없으면 null)
        /// </summary>
        public EditorBarData GetBar(int barNumber)
        {
            return bars.TryGetValue(barNumber, out var bar) ? bar : null;
        }

        /// <summary>
        /// 모든 마디 번호를 정렬된 순서로 반환
        /// </summary>
        public List<int> GetBarNumbers()
        {
            return bars.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// 마지막 마디 번호 반환 (마디가 없으면 0)
        /// </summary>
        public int GetLastBarNumber()
        {
            return bars.Count > 0 ? bars.Keys.Max() : 0;
        }

        /// <summary>
        /// 모든 데이터 초기화 (새로 만들기)
        /// </summary>
        public void Clear()
        {
            bars.Clear();
            title = "";
            artist = "";
            difficulty = Difficulty.Normal;
            level = 1;
            bpm = 120;
            audioClip = null;
            filePath = null;
            // 0번 마디는 항상 존재
            bars[0] = new EditorBarData(0);
        }

        /// <summary>
        /// 마디당 시간 (초) 계산
        /// </summary>
        public double GetBarDuration()
        {
            return (60.0 / bpm) * 4.0;
        }

        /// <summary>
        /// 전체 bars Dictionary 반환 (직렬화용)
        /// </summary>
        public Dictionary<int, EditorBarData> GetAllBars()
        {
            return bars;
        }
    }
}
