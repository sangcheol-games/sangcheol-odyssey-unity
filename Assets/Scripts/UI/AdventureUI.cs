using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
        private const int VISIBLE_COUNT = 7;                    // 화면에 보이는 슬롯 수
        private const int WHEEL_BUFFER = 2;                     // 위아래 숨김 버퍼 칸 수(회전 때 들어오고 나가는 슬롯)
        private const int SLOT_COUNT = VISIBLE_COUNT + WHEEL_BUFFER * 2;
        private const int CENTER_INDEX = SLOT_COUNT / 2;        // 0-based, 선택된 곡 슬롯
        private const float ALBUM_ART_SPIN_DURATION = 8f; // 앨범아트 1바퀴에 걸리는 시간(초)
        private const float MAX_WHEEL_OFFSET = WHEEL_BUFFER;    // 연타 시 밀려 있을 수 있는 최대 칸 수. 버퍼 칸 수를 넘으면 끝에 빈칸이 보인다

        // 바퀴 회전 틱 효과음. 지금 원샷 뱅크가 타격음용이라 StreamingAssets/HitSound/ 기준이고 HitSound 버스로 나간다.
        // 파일이 없으면 Register가 경고 후 None을 돌려주고 Play는 무시한다(애니메이션만 동작).
        // TODO: UI SFX 전용 원샷/버스가 생기면 옮긴다.
        private const string WHEEL_TICK_SOUND_FILE = "ui_wheel_tick.wav";

        [Header("곡 리스트 바퀴")]
        [SerializeField] private float wheelRadius = 800f;          // 바퀴 반지름. 중심은 리스트 오른쪽
        [SerializeField] private float wheelStepAngle = 9.2f;       // 슬롯 한 칸 사이 각도(도). 세로 간격 ≈ 반지름 × sin(각도)
        [SerializeField] private float wheelTickDuration = 0.16f;   // 한 칸 회전 시간(초)
        [SerializeField] private float wheelOvershoot = 2f;         // 한 칸을 넘어갔다 걸리는 정도(OutBack overshoot)
        [SerializeField, Range(0f, 1f)] private float edgeAlpha = 0.35f; // 보이는 양 끝 슬롯의 투명도

        private List<MusicSO> musicList;
        private MusicListUI[] slots;
        private RectTransform[] slotRects;
        private CanvasGroup[] slotCanvasGroups;
        private Transform musicListContainer;

        // 시각 오프셋(칸 단위). 데이터는 즉시 바꾸고 이 값을 ±1에서 0으로 트윈해 바퀴가 도는 것처럼 보이게 한다.
        private float wheelOffset;
        private Tween wheelTween;

        private IOneShotPlayer _oneShots;
        private OneShotId _wheelTickSound;

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

            // 회전 도중 화면을 떠나도 돌아왔을 때 정지 상태로 보이게 한다(UI 스택 재사용)
            StopWheel();
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

            // 슬롯 위치는 바퀴 배치(LayoutSlots)가 직접 정하므로 레이아웃 그룹은 끈다
            if (musicListContainer.TryGetComponent(out LayoutGroup layoutGroup))
                layoutGroup.enabled = false;

            // MusicListUI 프리팹 동적 생성(보이는 7개 + 위아래 숨김 버퍼)
            slots = new MusicListUI[SLOT_COUNT];
            slotRects = new RectTransform[SLOT_COUNT];
            slotCanvasGroups = new CanvasGroup[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                GameObject go = ResourceLoader.PrefabInstantiate("UI/MusicListUI", musicListContainer);
                slots[i] = go.AddComponent<MusicListUI>();

                // 컨테이너 중앙 기준으로 배치
                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                slotRects[i] = rect;

                if (!go.TryGetComponent(out CanvasGroup canvasGroup))
                    canvasGroup = go.AddComponent<CanvasGroup>();
                slotCanvasGroups[i] = canvasGroup;
            }

            // 바퀴 틱 효과음 id 확보. Register는 파일명 기준 멱등이다.
            if (ServiceLocator.TryGet<IOneShotPlayer>(out _oneShots))
                _wheelTickSound = _oneShots.Register(WHEEL_TICK_SOUND_FILE);

            selectedIndex = 0;
            wheelOffset = 0f;
            RefreshList();
            LayoutSlots();
            OnSelectedMusicChanged();
        }

        /// <summary>
        /// 원형 큐 방식으로 슬롯(보이는 7개 + 위아래 버퍼)에 곡 데이터를 표시합니다.
        /// </summary>
        private void RefreshList()
        {
            if (musicList == null || musicList.Count == 0) return;

            for (int i = 0; i < SLOT_COUNT; i++)
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

        /// <summary>
        /// 슬롯을 오른쪽에 중심을 둔 원의 호를 따라 배치합니다(관람차 곤돌라처럼 항상 똑바로).
        /// p = 0(선택 슬롯)이 가장 왼쪽, |p|가 클수록 오른쪽으로 휘어 들어갑니다.
        /// </summary>
        private void LayoutSlots()
        {
            if (slotRects == null) return;

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                float p = (i - CENTER_INDEX) + wheelOffset;
                float theta = p * wheelStepAngle * Mathf.Deg2Rad;

                float x = wheelRadius * (1f - Mathf.Cos(theta));
                float y = -wheelRadius * Mathf.Sin(theta); // p가 클수록 아래
                slotRects[i].anchoredPosition = new Vector2(x, y);

                slotCanvasGroups[i].alpha = GetSlotAlpha(Mathf.Abs(p));
            }
        }

        /// <summary>
        /// 가운데 5칸은 불투명, 보이는 양 끝 칸은 edgeAlpha, 그 바깥 버퍼 칸은 0. 회전 중에는 그 사이를 선형 보간합니다.
        /// </summary>
        private float GetSlotAlpha(float distance)
        {
            float edgeDistance = VISIBLE_COUNT / 2;      // 3: 보이는 양 끝 칸
            float innerDistance = edgeDistance - 1f;     // 2: 불투명 구간 끝
            float hiddenDistance = edgeDistance + 1f;    // 4: 첫 숨김 버퍼 칸

            if (distance <= innerDistance)
                return 1f;

            if (distance <= edgeDistance)
                return Mathf.Lerp(1f, edgeAlpha, distance - innerDistance);

            if (distance <= hiddenDistance)
                return Mathf.Lerp(edgeAlpha, 0f, distance - edgeDistance);

            return 0f;
        }

        /// <summary>
        /// 바퀴를 한 칸 돌립니다. 데이터는 이미 바뀐 상태이므로 오프셋을 ±1 밀어 직전 위치처럼 보이게 한 뒤
        /// 0으로 트윈합니다. OutBack으로 살짝 넘어갔다 걸리는 '찰칵' 느낌을 냅니다.
        /// 트윈 중 다시 들어오면 현재 오프셋에 이어 붙여 연타해도 끊기지 않습니다.
        /// </summary>
        /// <param name="step">+1 = 다음 곡(리스트가 위로), -1 = 이전 곡(리스트가 아래로)</param>
        private void StartWheelTick(int step)
        {
            wheelTween?.Kill();

            wheelOffset = Mathf.Clamp(wheelOffset + step, -MAX_WHEEL_OFFSET, MAX_WHEEL_OFFSET);
            LayoutSlots();

            wheelTween = DOTween.To(() => wheelOffset, SetWheelOffset, 0f, wheelTickDuration)
                .SetEase(Ease.OutBack, wheelOvershoot)
                .SetLink(gameObject);

            if (_oneShots != null)
                _oneShots.Play(_wheelTickSound);
        }

        private void SetWheelOffset(float value)
        {
            wheelOffset = value;
            LayoutSlots();
        }

        /// <summary>
        /// 회전을 멈추고 정지 위치로 되돌립니다.
        /// </summary>
        private void StopWheel()
        {
            wheelTween?.Kill();
            wheelTween = null;
            wheelOffset = 0f;
            LayoutSlots();
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

            // 상하 이동은 바퀴를 한 칸 돌린다(아래 = 다음 곡 = 리스트가 위로)
            if (direction.y > 0)
                StartWheelTick(-1);
            else if (direction.y < 0)
                StartWheelTick(1);

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
