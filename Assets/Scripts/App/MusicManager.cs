using System.Collections.Generic;
using System.Linq;
using SCOdyssey.Domain.Entity;
using UnityEngine;

namespace SCOdyssey.App
{
    public class MusicManager : IMusicManager
    {
        private List<MusicSO> musicList;
        private MusicSO currentMusic;

        public MusicManager()
        {
            LoadMusicList();
        }

        /// <summary>
        /// Resources/Music 폴더에서 모든 MusicSO 로드
        /// TODO: 나중에 서버에서 받아오는 방식으로 변경
        /// </summary>
        private void LoadMusicList()
        {
            MusicSO[] musicArray = Resources.LoadAll<MusicSO>("Music");
            musicList = musicArray.ToList();

            if (musicList.Count > 0)
            {
                Debug.Log($"[MusicManager] Loaded {musicList.Count} music(s) from Resources/Music");
            }
            else
            {
                Debug.LogWarning("[MusicManager] No music found in Resources/Music folder!");
            }
        }

        /// <summary>
        /// 음악을 선택합니다.
        /// </summary>
        public void SelectMusic(MusicSO music)
        {
            if (music == null)
            {
                Debug.LogError("[MusicManager] Null MusicSO provided!");
                return;
            }

            currentMusic = music;
            Debug.Log($"[MusicManager] Selected Music: {music.title[Domain.Service.Constants.Language.KR]}");
        }

        /// <summary>
        /// 현재 선택된 음악을 반환합니다.
        /// </summary>
        public MusicSO GetCurrentMusic() => currentMusic;

        /// <summary>
        /// 선택된 음악을 초기화합니다.
        /// </summary>
        public void ClearSelection()
        {
            currentMusic = null;
            Debug.Log("[MusicManager] Music selection cleared");
        }

        /// <summary>
        /// 모든 음악 리스트를 반환합니다.
        /// </summary>
        public List<MusicSO> GetMusicList() => musicList;
    }
}

