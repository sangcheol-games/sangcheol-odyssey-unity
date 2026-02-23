using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    public class AdventureUI : BaseUI
    {
        private const int DISPLAY_COUNT = 7;
        private const int CENTER_INDEX = 3; // 0-based, 4번째 슬롯

        private List<MusicSO> musicList;
        private MusicListUI[] slots;
        private Transform musicListContainer;

        private int selectedIndex;
        private Difficulty selectedDifficulty = Difficulty.Easy;

        private enum Images
        {
            AlbumArt      // 앨범 아트
        }

        protected override void Awake()
        {
            base.Awake();
            BindImage(typeof(Images));
            Init();
        }

        private void Init()
        {
            var musicManager = ServiceLocator.Get<IMusicManager>();
            musicList = musicManager.GetMusicList();

            // MusicList 컨테이너 찾기
            musicListContainer = transform.Find("MusicList");
            if (musicListContainer == null)
            {
                Debug.LogError("[AdventureUI] MusicList container not found!");
                return;
            }

            // 기존 자식 오브젝트 제거
            for (int i = musicListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(musicListContainer.GetChild(i).gameObject);
            }

            // MusicListUI 프리팹 7개 동적 생성
            slots = new MusicListUI[DISPLAY_COUNT];
            for (int i = 0; i < DISPLAY_COUNT; i++)
            {
                GameObject go = ResourceLoader.PrefabInstantiate("UI/MusicListUI", musicListContainer);
                var slot = go.AddComponent<MusicListUI>();
                slot.Init();
                slots[i] = slot;
            }

            selectedIndex = 0;
            RefreshList();
        }

        /// <summary>
        /// 원형 큐 방식으로 7개 슬롯에 곡 데이터를 표시합니다.
        /// </summary>
        private void RefreshList()
        {
            if (musicList == null || musicList.Count == 0) return;

            for (int i = 0; i < DISPLAY_COUNT; i++)
            {
                int dataIndex = WrapIndex(selectedIndex - CENTER_INDEX + i);
                slots[i].SetData(musicList[dataIndex]);
                slots[i].SetSelected(i == CENTER_INDEX, selectedDifficulty);
            }

            // 곡 앨범아트 갱신
            var selectedMusic = musicList[selectedIndex];
            GetImage((int)Images.AlbumArt).sprite = selectedMusic.albumArt;
        }

        /// <summary>
        /// 음수 인덱스도 올바르게 순환하는 래핑 함수
        /// </summary>
        private int WrapIndex(int index)
        {
            int count = musicList.Count;
            return ((index % count) + count) % count;
        }

        protected override void HandleSelect(Vector2 direction)
        {
            if (musicList == null || musicList.Count == 0) return;

            // 상하: 곡 선택 이동 (원형 큐)
            if (direction.y > 0)
                selectedIndex = WrapIndex(selectedIndex - 1);
            else if (direction.y < 0)
                selectedIndex = WrapIndex(selectedIndex + 1);

            // 좌우: 난이도 선택
            if (direction.x > 0 && selectedDifficulty < Difficulty.Extreme)
                selectedDifficulty++;
            else if (direction.x < 0 && selectedDifficulty > Difficulty.Easy)
                selectedDifficulty--;

            RefreshList();
        }

        protected override void HandleSubmit()
        {
            if (musicList == null || musicList.Count == 0) return;

            var musicManager = ServiceLocator.Get<IMusicManager>();
            musicManager.SelectMusic(musicList[selectedIndex]);
            musicManager.SelectDifficulty(selectedDifficulty);

            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);

            SceneManager.LoadScene("GameScene");
        }

        protected override void HandleCancel()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
        }
    }
}
