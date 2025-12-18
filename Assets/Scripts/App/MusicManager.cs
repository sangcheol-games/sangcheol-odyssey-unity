using System.Collections.Generic;
using SCOdyssey.Domain.Entity;
using UnityEngine;

namespace SCOdyssey.App
{
    public class MusicManager : MonoBehaviour
    {
        public static MusicManager Instance { get; private set; }

        public List<MusicSO> musicList;     // TODO: 서버에서 리스트를 받아와 할당하는 작업 필요

        private MusicSO currentMusic;       // 현재 선택된 음악


        public void SelectMusic(int id)
        {
            //TODO
        }

        public MusicSO GetCurrentMusic() => currentMusic;
    }
}
