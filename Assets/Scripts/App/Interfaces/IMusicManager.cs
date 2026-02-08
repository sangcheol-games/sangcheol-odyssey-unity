using System.Collections.Generic;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App
{
    public interface IMusicManager
    {
        /// <summary>
        /// 음악을 선택합니다.
        /// </summary>
        void SelectMusic(MusicSO music);

        /// <summary>
        /// 현재 선택된 음악을 반환합니다.
        /// </summary>
        MusicSO GetCurrentMusic();

        /// <summary>
        /// 선택된 음악을 초기화합니다.
        /// </summary>
        void ClearSelection();

        /// <summary>
        /// 모든 음악 리스트를 반환합니다.
        /// </summary>
        List<MusicSO> GetMusicList();
    }
}

