namespace SCOdyssey.ChartEditor.Data
{
    /// <summary>
    /// 에디터에서 한 마디의 데이터를 관리하는 클래스
    /// </summary>
    public class EditorBarData
    {
        public int barNumber;
        public int beat;    // 비트 분할수 (기본 4)

        // 방향 설정 (null = 미설정, true = LTR, false = RTL)
        public bool? upperGroupLTR;   // 상단 그룹 (레인 1, 2)
        public bool? lowerGroupLTR;   // 하단 그룹 (레인 3, 4)

        // 4개 레인의 노트 시퀀스 (laneSequences[0] = 레인1, ..., laneSequences[3] = 레인4)
        // 각 배열은 beat 길이이며, 값은 '0'~'4' (NoteType)
        // 화면상 왼쪽→오른쪽 순서로 저장
        public char[][] laneSequences;

        public EditorBarData(int barNumber, int beat = 4)
        {
            this.barNumber = barNumber;
            this.beat = beat;
            this.upperGroupLTR = null;
            this.lowerGroupLTR = null;

            laneSequences = new char[4][];
            for (int i = 0; i < 4; i++)
            {
                laneSequences[i] = new char[beat];
                for (int j = 0; j < beat; j++)
                {
                    laneSequences[i][j] = '0';
                }
            }
        }

        /// <summary>
        /// 해당 레인의 방향이 설정되어 있는지 확인
        /// </summary>
        public bool IsDirectionSet(int laneNumber)
        {
            return laneNumber <= 2 ? upperGroupLTR.HasValue : lowerGroupLTR.HasValue;
        }

        /// <summary>
        /// 해당 레인의 방향 반환 (true = LTR, false = RTL)
        /// IsDirectionSet이 true일 때만 호출
        /// </summary>
        public bool GetDirection(int laneNumber)
        {
            return laneNumber <= 2 ? upperGroupLTR.Value : lowerGroupLTR.Value;
        }

        /// <summary>
        /// 비트 수 변경. 기존 노트 데이터를 새 비트 수에 맞게 비율 스케일하여 재배치.
        /// LTR 예: 4비트 "0101" → 8비트 "00100010" (노트 위치를 2배 간격으로 확장)
        /// RTL 예: 4비트 "0101" → 8비트 "00010001" (시간 기준으로 변환 후 역방향 배열에 저장)
        /// RTL에서는 array[i] = 화면 우측(시간 시작)부터 역순으로 저장되므로,
        /// 시간축 기준으로 스케일한 뒤 다시 역방향으로 변환.
        /// 비배수 변환(예: 4→6)은 AwayFromZero 반올림으로 가장 가까운 위치에 배치.
        /// 충돌 시 뒤에 처리된 노트가 앞 노트를 덮어씀. 범위 밖 노트는 손실.
        /// </summary>
        public void SetBeat(int newBeat)
        {
            if (newBeat == beat) return;

            char[][] newSequences = new char[4][];
            for (int i = 0; i < 4; i++)
            {
                newSequences[i] = new char[newBeat];
                // 초기값 '0'으로 초기화
                for (int j = 0; j < newBeat; j++)
                    newSequences[i][j] = '0';

                // 레인 방향 결정 (레인 인덱스 0,1 = 상단 그룹, 2,3 = 하단 그룹)
                bool? isLTR = i < 2 ? upperGroupLTR : lowerGroupLTR;

                // 기존 노트를 비율에 맞는 새 위치로 이전
                // AwayFromZero: 0.5 이상이면 항상 올림 (은행가 반올림 방지)
                for (int oldIdx = 0; oldIdx < beat; oldIdx++)
                {
                    if (laneSequences[i][oldIdx] == '0') continue;

                    int newIdx;
                    if (isLTR == false)
                    {
                        // RTL: 배열 인덱스가 시간과 역방향 (array[0]=시간 끝, array[beat-1]=시간 시작)
                        // 시간 기준으로 변환: old_time = (beat-1) - oldIdx
                        int oldTime = (beat - 1) - oldIdx;
                        int newTime = (int)System.Math.Round(
                            (double)oldTime * newBeat / beat,
                            System.MidpointRounding.AwayFromZero);
                        // 새 배열 인덱스로 역변환: newIdx = (newBeat-1) - new_time
                        newIdx = (newBeat - 1) - newTime;
                    }
                    else
                    {
                        // LTR 또는 방향 미설정: 배열 인덱스 = 시간 순서와 동일
                        newIdx = (int)System.Math.Round(
                            (double)oldIdx * newBeat / beat,
                            System.MidpointRounding.AwayFromZero);
                    }

                    if (newIdx >= 0 && newIdx < newBeat)
                        newSequences[i][newIdx] = laneSequences[i][oldIdx];
                }
            }

            laneSequences = newSequences;
            beat = newBeat;
        }

        /// <summary>
        /// 해당 레인에 노트가 하나라도 있는지 확인
        /// </summary>
        public bool HasAnyNote(int laneIndex)
        {
            for (int i = 0; i < beat; i++)
            {
                if (laneSequences[laneIndex][i] != '0') return true;
            }
            return false;
        }

        /// <summary>
        /// 해당 레인에 방향이 설정되어 있고 (노트가 있거나 없거나) 유효한 레인인지 확인
        /// 방향 설정됨 = 판정선이 이동하는 유효한 레인
        /// </summary>
        public bool IsLaneActive(int laneNumber)
        {
            return IsDirectionSet(laneNumber);
        }
    }
}
