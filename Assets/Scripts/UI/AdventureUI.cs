using System.Collections;
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
        private MusicSO selectedMusic => musicList[selectedIndex];
        private Difficulty selectedDifficulty = Difficulty.Easy;

        private enum Images
        {
            AlbumArt      // 앨범 아트
        }

        private enum Buttons
        {
            BackButton    // 뒤로가기
        }

        protected override void Awake()
        {
            base.Awake();
            BindImage(typeof(Images));
            BindButton(typeof(Buttons));

            GetButton((int)Buttons.BackButton).onClick.AddListener(OnClickBackButton);

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
                slots[i] = go.AddComponent<MusicListUI>();
            }

            selectedIndex = 0;
            RefreshList();

            StartCoroutine(PlayPreviewAudio());
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
                slots[i].SetData(musicList[dataIndex], i == CENTER_INDEX, selectedDifficulty);
            }

            // 곡 앨범아트 갱신
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

        private void OnClickBackButton()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
        }

        private IEnumerator PlayPreviewAudio()
        {
            var audioManager = ServiceLocator.Get<IAudioManager>();
            if(audioManager.IsPlaying) audioManager.Stop();

            var audioFilePath = selectedMusic.previewAudioFilePath;

            if(string.IsNullOrEmpty(audioFilePath))
            {
                Debug.LogWarning("[AdventureUI] previewAudioFilePath is empty!");
                yield break;
            }
            else
            {
                audioManager.LoadAudio(audioFilePath);
                // NONBLOCKING 로드 완료까지 대기 (보통 1-3프레임)
                while(!audioManager.IsLoaded) yield return null;

                var dspStartTime = audioManager.GetDSPTime();
                audioManager.PlayScheduled(dspStartTime);
            }
        }

        protected override void HandleSelect(Vector2 direction)
        {
            if (musicList == null || musicList.Count == 0) return;

            // 상하: 곡 선택 이동 (원형 큐)
            var isMusicChanged = direction.y != 0;
            if (direction.y > 0)
                selectedIndex = WrapIndex(selectedIndex - 1);
            else if (direction.y < 0)
                selectedIndex = WrapIndex(selectedIndex + 1);

            // 좌우: 난이도 선택 (level -1인 난이도는 스킵)
            if (direction.x > 0)
            {
                for (Difficulty d = selectedDifficulty + 1; d <= Difficulty.Extreme; d++)
                    if (IsAvailable(selectedMusic, d)) { selectedDifficulty = d; break; }
            }
            else if (direction.x < 0)
            {
                for (Difficulty d = selectedDifficulty - 1; d >= Difficulty.Easy; d--)
                    if (IsAvailable(selectedMusic, d)) { selectedDifficulty = d; break; }
            }

            RefreshList();

            if(isMusicChanged)
                StartCoroutine(PlayPreviewAudio());
        }

        protected override void HandleSubmit()
        {
            if (musicList == null || musicList.Count == 0) return;

            // 준비되지 않은 난이도는 선택 불가
            if (!IsAvailable(selectedMusic, selectedDifficulty)) return;

            var musicManager = ServiceLocator.Get<IMusicManager>();
            musicManager.SelectMusic(selectedMusic);
            musicManager.SelectDifficulty(selectedDifficulty);

            SceneManager.LoadScene("GameScene");
        }

        private bool IsAvailable(MusicSO music, Difficulty diff)
        {
            return music.level != null && music.level.TryGetValue(diff, out int lv) && lv != -1;
        }

        protected override void HandleCancel()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
        }
    }
}
