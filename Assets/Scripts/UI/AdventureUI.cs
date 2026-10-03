using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;
using Unity.VisualScripting;

namespace SCOdyssey.UI
{
    public class AdventureUI : BaseUI
    {
        private const int DISPLAY_COUNT = 7;
        private const int CENTER_INDEX = 3; // 0-based, 4번째 슬롯
        private const float ALBUM_ART_SPIN_DURATION = 8f; // 앨범아트 1바퀴에 걸리는 시간(초)

        private List<MusicSO> musicList;
        private MusicListUI[] slots;
        private Transform musicListContainer;

        private RectTransform albumArtRect;
        private float albumArtAngle; // 시계방향 누적 각도(양수, 0~360)

        // 프리뷰 요청 취소용(화면을 떠나면 취소). 곡을 빠르게 넘기면 재생기가 마지막 요청만 남긴다(150ms 디바운스).
        private CancellationTokenSource _previewCts;

        private int selectedIndex;
        private MusicSO selectedMusic => musicList[selectedIndex];
        private Difficulty selectedDifficulty = Difficulty.Easy;

        private const string EMPTY_RECORD_TEXT = "- - -"; // 기록 없는 곡+난이도 표시

        private enum Texts
        {
            BestScore,      // 최고 점수
            BestCombo,      // 최고 콤보
            BestRate,       // 최고 점수비율
            BestClearType   // 최고 클리어 타입
        }

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
            BindText(typeof(Texts));
            BindImage(typeof(Images));
            BindButton(typeof(Buttons));

            GetButton((int)Buttons.BackButton).onClick.AddListener(OnClickBackButton);

            albumArtRect = GetImage((int)Images.AlbumArt).rectTransform;

            Init();
        }

        private void Update()
        {
            if (albumArtRect == null) return;

            // 시계방향 = 음수 Z (UI 좌표계 기준)
            albumArtAngle = Mathf.Repeat(albumArtAngle + (360f / ALBUM_ART_SPIN_DURATION) * Time.deltaTime, 360f);
            albumArtRect.localRotation = Quaternion.Euler(0f, 0f, -albumArtAngle);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            // 결과 화면에서 돌아오면 이 인스턴스가 재사용되므로(UI 스택 유지) 방금 갱신된 기록을 다시 그린다
            RefreshRecord();
            PlayPreviewAudio();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // stop preview audio when ui change
            CancelPreview();
            if (ServiceLocator.TryGet<IMusicPlayers>(out var music)) music.Preview.Stop();
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
            OnSelectedMusicChanged();
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

            // 곡 이동과 난이도 변경 모두 이 경로를 지난다
            RefreshRecord();
        }

        /// <summary>
        /// 현재 선택된 곡+난이도의 최고기록을 표시합니다.
        /// 프리팹에 텍스트가 없으면(바인딩 실패) 해당 항목만 건너뜁니다.
        /// </summary>
        private void RefreshRecord()
        {
            if (musicList == null || musicList.Count == 0) return;
            if (!ServiceLocator.TryGet<IUserDataManager>(out var userDataManager)) return;

            string scoreText = EMPTY_RECORD_TEXT;
            string comboText = EMPTY_RECORD_TEXT;
            string rateText = EMPTY_RECORD_TEXT;
            string clearTypeText = EMPTY_RECORD_TEXT;

            if (userDataManager.TryGetRecord(selectedMusic.id, selectedDifficulty, out var record))
            {
                scoreText = record.bestScore.ToString("N0");
                comboText = record.bestCombo.ToString();
                rateText = $"{record.bestRate:F2}%";
                clearTypeText = record.bestClearType.ToString();
            }

            SetRecordText(Texts.BestScore, scoreText);
            SetRecordText(Texts.BestCombo, comboText);
            SetRecordText(Texts.BestRate, rateText);
            SetRecordText(Texts.BestClearType, clearTypeText);
        }

        private void SetRecordText(Texts textType, string value)
        {
            var text = GetText((int)textType);
            if (text == null) return;

            text.text = value;
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

        private void OnSelectedMusicChanged()
        {
            // 곡 앨범아트 갱신
            GetImage((int)Images.AlbumArt).sprite = selectedMusic.albumArt;

            // 곡이 바뀌면 회전을 0도부터 다시 시작
            albumArtAngle = 0f;
            albumArtRect.localRotation = Quaternion.identity;

            PlayPreviewAudio();
        }

        private void PlayPreviewAudio()
        {
            if (!isActiveAndEnabled || musicList == null || musicList.Count == 0) return;
            if (!ServiceLocator.TryGet<IMusicPlayers>(out var music))
            {
                Debug.LogWarning("[AdventureUI] IMusicPlayers를 찾지 못해 프리뷰를 재생하지 않습니다.");
                return;
            }

            var audioFilePath = selectedMusic.previewAudioFilePath;
            if (string.IsNullOrEmpty(audioFilePath))
            {
                // 이전 곡 프리뷰가 남지 않게 멈춘다. TODO: 공용 알림 UI가 생기면 화면에 안내한다.
                music.Preview.Stop();
                Debug.LogWarning("[AdventureUI] 프리뷰 음원이 없습니다: " + selectedMusic.name);
                return;
            }

            if (_previewCts == null) _previewCts = new CancellationTokenSource();
            PlayPreviewAsync(music.Preview, audioFilePath, _previewCts.Token).Forget();
        }

        private static async UniTaskVoid PlayPreviewAsync(IMusicPlayer preview, string fileName, CancellationToken ct)
        {
            AudioLoadResult result = await preview.PlayAsync(fileName, true, ct);
            if (result.Status == AudioLoadStatus.NotFound || result.Status == AudioLoadStatus.DecodeError || result.Status == AudioLoadStatus.Timeout)
            {
                // TODO: 공용 알림 UI가 생기면 화면에 안내한다.
                Debug.LogWarning("[AdventureUI] 프리뷰를 재생하지 못했습니다(" + result.Status + "): " + result.Detail);
            }
        }

        private void CancelPreview()
        {
            if (_previewCts == null) return;
            _previewCts.Cancel();
            _previewCts.Dispose();
            _previewCts = null;
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
                OnSelectedMusicChanged();
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
