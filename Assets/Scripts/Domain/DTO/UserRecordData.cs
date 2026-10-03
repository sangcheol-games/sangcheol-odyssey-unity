using System;
using System.Collections.Generic;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Domain.Dto
{
    // 곡 + 난이도 하나의 최고기록. 네 항목은 서로 다른 판에서 나온 값일 수 있다(항목별로 따로 최고값 갱신).
    [Serializable]
    public class UserMusicRecord
    {
        public int musicId;                 // MusicSO.id (LocalizedString 제목은 비동기 로드 전 빈 문자열이라 키로 못 쓴다)
        public Difficulty difficulty;
        public int bestScore;
        public int bestCombo;
        public float bestRate;              // 점수비율 0 ~ 100 (ScoreManager.GetGaugePercent, 결과 화면 GaugeText와 같은 값)
        public ClearType bestClearType;     // enum 순서(Fail < Clear < FullCombo < OverMillion < AllPerfect)로 대소 비교
    }

    // records.json 루트. JsonUtility가 Dictionary·튜플을 직렬화하지 못해 List로 감싼다.
    [Serializable]
    public class UserRecordData
    {
        public int version = 1;             // 포맷 변경·서버 이관 시 마이그레이션 근거
        public List<UserMusicRecord> records = new List<UserMusicRecord>();
    }
}
