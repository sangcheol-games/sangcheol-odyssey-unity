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
        /// 비트 수 변경. 기존 노트 데이터를 새 비트 수에 맞게 재할당
        /// 새 배열이 더 짧으면 초과 위치의 노트 손실
        /// </summary>
        public void SetBeat(int newBeat)
        {
            if (newBeat == beat) return;

            char[][] newSequences = new char[4][];
            for (int i = 0; i < 4; i++)
            {
                newSequences[i] = new char[newBeat];
                for (int j = 0; j < newBeat; j++)
                {
                    // 기존 데이터 범위 내면 복사, 아니면 빈 노트
                    newSequences[i][j] = (j < beat) ? laneSequences[i][j] : '0';
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
