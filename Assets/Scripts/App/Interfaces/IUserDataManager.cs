using SCOdyssey.Domain.Dto;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.App
{
    public interface IUserDataManager
    {
        /// <summary>
        /// 저장된 기록을 불러옵니다. 부팅 시 한 번 호출합니다.
        /// </summary>
        void Load();

        /// <summary>
        /// 곡 + 난이도의 최고기록을 조회합니다. 한 번도 끝까지 플레이하지 않았으면 false.
        /// </summary>
        bool TryGetRecord(int musicId, Difficulty difficulty, out UserMusicRecord record);

        /// <summary>
        /// 이번 판 결과를 항목별 최고값으로 합칩니다. 하나라도 갱신되면 저장하고 true를 반환합니다.
        /// </summary>
        bool SubmitResult(int musicId, Difficulty difficulty, int score, int maxCombo, float rate, ClearType clearType);
    }
}
