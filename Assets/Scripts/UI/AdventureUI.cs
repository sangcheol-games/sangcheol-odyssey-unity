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
        private const int VISIBLE_COUNT = 9;                    // 화면에 보이는 슬롯 수
        private const int WHEEL_BUFFER = 2;                     // 위아래 숨김 버퍼 칸 수(회전 때 들어오고 나가는 슬롯)
        private const int SLOT_COUNT = VISIBLE_COUNT + WHEEL_BUFFER * 2;
        private const int CENTER_INDEX = SLOT_COUNT / 2;        // 0-based, 선택된 곡 슬롯
        private const float LP_SPIN_DURATION = 8f; // LP판 1바퀴에 걸리는 시간(초)
        private const float MAX_WHEEL_OFFSET = WHEEL_BUFFER;    // 연타 시 밀려 있을 수 있는 최대 칸 수. 버퍼 칸 수를 넘으면 끝에 빈칸이 보인다
        private const int DIFFICULTY_COUNT = 4;                 // Difficulty enum 개수(Easy~Extreme). 난이도 버튼 수

        // 효과음. StreamingAssets/Sfx/ 기준이고 Sfx 버스(효과음 볼륨)로 나간다.
        // 파일이 없으면 Register가 경고 후 None을 돌려주고 Play는 무시한다(애니메이션만 동작).
        private const string WHEEL_TICK_SOUND_FILE = "ui_wheel_tick.wav";               // 곡 변경(바퀴 한 칸)
        private const string DIFFICULTY_CHANGE_SOUND_FILE = "ui_difficulty_change.wav"; // 난이도 변경

        [Header("곡 리스트 바퀴")]
        [SerializeField] private float wheelRadius = 800f;          // 바퀴 반지름. 중심은 리스트 오른쪽
        [SerializeField] private float wheelStepAngle = 7f;         // 슬롯 한 칸 사이 각도(도). 오른쪽으로 휘어 들어가는 정도만 정한다
        [SerializeField] private float wheelSlotSpacing = 102f;     // 슬롯 세로 간격. 패널 높이(90)보다 커야 겹치지 않는다
        [SerializeField] private float selectedSlotOffset = 40f;    // 선택 슬롯을 호보다 왼쪽으로 더 내미는 거리. 호만으로는 옆 칸과 10 정도밖에 차이 나지 않는다
        [SerializeField] private float wheelTickDuration = 0.16f;   // 한 칸 회전 시간(초)
        [SerializeField] private float wheelOvershoot = 2f;         // 한 칸을 넘어갔다 걸리는 정도(OutBack overshoot)

        [Header("곡 리스트 홀드 반복")]
        [Tooltip("위/아래를 누른 뒤 연속 이동이 시작되기까지의 시간(초)")]
        [SerializeField] private float holdRepeatDelay = 0.35f;
        [Tooltip("연속 이동 첫 간격(초). 누르고 있을수록 '최소 간격'까지 줄어든다")]
        [SerializeField] private float holdRepeatStartInterval = 0.12f;
        [Tooltip("가속이 끝난 뒤의 연속 이동 간격(초)")]
        [SerializeField] private float holdRepeatMinInterval = 0.04f;
        [Tooltip("연속 이동이 시작된 뒤 '최소 간격'까지 빨라지는 데 걸리는 시간(초)")]
        [SerializeField] private float holdRepeatAccelTime = 1.5f;

        [Header("난이도 버튼")]
        [SerializeField] private float selectedDifficultyRaise = 20f;       // 선택된 난이도 버튼이 올라가는 높이
        [SerializeField] private float difficultyRaiseDuration = 0.12f;     // 올라가고 내려오는 시간(초)
        [SerializeField, Range(0f, 1f)] private float unavailableDifficultyAlpha = 0.4f; // 곡에 없는 난이도(레벨 -1) 버튼의 투명도

        private List<MusicSO> musicList;
        private MusicListUI[] slots;
        private RectTransform[] slotRects;
        private CanvasGroup[] slotCanvasGroups;
        private Transform musicListContainer;

        // 시각 오프셋(칸 단위). 데이터는 즉시 바꾸고 이 값을 ±1에서 0으로 트윈해 바퀴가 도는 것처럼 보이게 한다.
        private float wheelOffset;
        private Tween wheelTween;

        // 방향키 홀드 상태. Select 액션은 값이 바뀔 때만 이벤트가 오므로 누르고 있는 동안은 Update에서 폴링한다
        private int holdDirX;           // 눌려 있는 좌우 방향(-1/0/1). 이미 눌린 축이 다른 키 때문에 다시 이벤트로 와도 무시하기 위함
        private int holdDirY;           // 반복 중인 상하 방향(-1/0/1)
        private float holdElapsed;      // 상하를 누른 뒤 경과 시간(초)
        private float nextRepeatAt;     // 다음 연속 이동 시각(holdElapsed 기준)

        private ISfxPlayer _sfx;
        private OneShotId _wheelTickSound;
        private OneShotId _difficultyChangeSound;

        private RectTransform lpRect;
        private float lpAngle; // 시계방향 누적 각도(양수, 0~360)

        // 프리뷰 요청 취소용(화면을 떠나면 취소). 곡을 빠르게 넘기면 재생기가 마지막 요청만 남긴다(150ms 디바운스).
        private CancellationTokenSource _previewCts;

        private int selectedIndex;
        private MusicSO selectedMusic => musicList[selectedIndex];
        private Difficulty selectedDifficulty = Difficulty.Easy;   // 지금 곡에 실제로 적용된 난이도(시작·기록 조회 기준)
        private Difficulty preferredDifficulty = Difficulty.Easy;  // 사용자가 좌우로 고른 난이도. 곡에 없으면 selectedDifficulty만 가까운 난이도로 옮긴다

        // 난이도 버튼. 인덱스 = Difficulty enum 순서
        private RectTransform[] difficultyButtonRects;
        private CanvasGroup[] difficultyButtonGroups;
        private float[] difficultyButtonBaseY;      // 레이아웃 그룹이 정한 원래 높이. 선택되면 여기서 selectedDifficultyRaise만큼 올라간다
        private Tween[] difficultyButtonTweens;

        private const string EMPTY_RECORD_TEXT = "- - -"; // 기록 없는 곡+난이도 표시

        private enum Texts
        {
            BestScore,      // 최고 점수
            BestCombo,      // 최고 콤보
            BestRate,       // 최고 점수비율
            TitleText,      // 곡 제목
            ArtistText,     // 아티스트
            BPMText,        // BPM
            // 난이도 버튼 레벨. Difficulty enum 순서와 같아야 함(Level_Easy + (int)difficulty로 찾는다)
            Level_Easy,
            Level_Normal,
            Level_Hard,
            Level_Extreme
        }

        private enum Images
        {
            CaseAlbumArt, // 앨범 아트 (케이스 속, 정지. 케이스 그림에 맞춘 프리팹 기울기를 유지)
            LP,           // 턴테이블 LP판 (라벨째 회전)
            LPAlbumArt,   // LP 라벨 앨범 아트
            // 난이도 버튼. Difficulty enum 순서와 같아야 함(ButtonEasy + (int)difficulty로 찾는다)
            ButtonEasy,
            ButtonNormal,
            ButtonHard,
            ButtonExtreme
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

            // 바인딩이 어긋나도 Init까지는 돌아야 곡 리스트가 뜬다(LP 회전만 빠진다)
            Image lp = GetImage((int)Images.LP);
            if (lp != null)
                lpRect = lp.rectTransform;

            InitDifficultyButtons();
            Init();
        }

        private void Update()
        {
            UpdateHoldRepeat();

            if (lpRect == null) return;

            // 시계방향 = 음수 Z (UI 좌표계 기준)
            lpAngle = Mathf.Repeat(lpAngle + (360f / LP_SPIN_DURATION) * Time.deltaTime, 360f);
            lpRect.localRotation = Quaternion.Euler(0f, 0f, -lpAngle);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            // 결과 화면에서 돌아오면 이 인스턴스가 재사용되므로(UI 스택 유지) 방금 갱신된 기록을 다시 그린다
            // 기록 영역뿐 아니라 슬롯의 클리어 타입 별도 바뀌므로 리스트째 갱신한다(RefreshList가 RefreshRecord까지 부른다)
            RefreshList(false);
            RefreshMusicInfo(); // 설정에서 곡 표시 언어를 바꾸고 돌아온 경우
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
            RefreshDifficulty(false); // 난이도 버튼 트윈도 같은 이유로 끊고 최종 위치로 스냅

            holdDirX = 0;
            holdDirY = 0;
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

            // MusicListUI 프리팹 동적 생성(보이는 9개 + 위아래 숨김 버퍼)
            slots = new MusicListUI[SLOT_COUNT];
            slotRects = new RectTransform[SLOT_COUNT];
            slotCanvasGroups = new CanvasGroup[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                GameObject go = ResourceLoader.PrefabInstantiate("UI/MusicListUI", musicListContainer);

                // 패널·클리어 타입 스프라이트를 프리팹 인스펙터에서 받으므로 프리팹에 붙은 컴포넌트를 쓴다
                if (!go.TryGetComponent(out MusicListUI slot))
                    slot = go.AddComponent<MusicListUI>();
                slots[i] = slot;

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

            // 효과음 id 확보. Register는 파일명 기준 멱등이다.
            if (ServiceLocator.TryGet<ISfxPlayer>(out _sfx))
            {
                _wheelTickSound = _sfx.Register(WHEEL_TICK_SOUND_FILE);
                _difficultyChangeSound = _sfx.Register(DIFFICULTY_CHANGE_SOUND_FILE);
            }

            selectedIndex = 0;
            wheelOffset = 0f;
            if (musicList.Count > 0)
                selectedDifficulty = ResolveDifficulty(selectedMusic, preferredDifficulty);
            RefreshList(false);
            LayoutSlots();
            OnSelectedMusicChanged();
        }

        /// <summary>
        /// 난이도 버튼의 기준 위치와 CanvasGroup을 준비합니다.
        /// Difficulty의 HorizontalLayoutGroup은 리빌드 때마다 자식 위치를 덮어써 올림 트윈을 되돌리므로,
        /// 한 번 배치시킨 뒤 끄고 그 위치를 기준으로 삼습니다(MusicList 컨테이너에서 레이아웃 그룹을 끄는 것과 같은 방식).
        /// </summary>
        private void InitDifficultyButtons()
        {
            difficultyButtonRects = new RectTransform[DIFFICULTY_COUNT];
            difficultyButtonGroups = new CanvasGroup[DIFFICULTY_COUNT];
            difficultyButtonBaseY = new float[DIFFICULTY_COUNT];
            difficultyButtonTweens = new Tween[DIFFICULTY_COUNT];

            Image firstButton = GetImage((int)Images.ButtonEasy);
            if (firstButton != null && firstButton.transform.parent is RectTransform container)
            {
                if (container.TryGetComponent(out LayoutGroup layoutGroup))
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
                    layoutGroup.enabled = false;
                }
            }

            for (int i = 0; i < DIFFICULTY_COUNT; i++)
            {
                Image button = GetImage((int)Images.ButtonEasy + i);
                if (button == null) continue;

                RectTransform rect = button.rectTransform;
                difficultyButtonRects[i] = rect;
                difficultyButtonBaseY[i] = rect.anchoredPosition.y;

                // 배지·레벨 숫자까지 함께 흐려지도록 버튼 단위로 알파를 준다
                if (!button.TryGetComponent(out CanvasGroup canvasGroup))
                    canvasGroup = button.gameObject.AddComponent<CanvasGroup>();
                difficultyButtonGroups[i] = canvasGroup;
            }
        }

        /// <summary>
        /// 원형 큐 방식으로 슬롯(보이는 9개 + 위아래 버퍼)에 곡 데이터를 표시합니다.
        /// </summary>
        /// <param name="animate">난이도 버튼을 트윈으로 움직일지. 입력에 의한 갱신만 true</param>
        private void RefreshList(bool animate)
        {
            if (musicList == null || musicList.Count == 0) return;
            if (slots == null) return; // MusicList 컨테이너를 못 찾으면 슬롯이 없다(Init 참고)

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                int dataIndex = WrapIndex(selectedIndex - CENTER_INDEX + i);
                slots[i].SetData(musicList[dataIndex], i == CENTER_INDEX);
            }

            // 곡 이동과 난이도 변경 모두 이 경로를 지난다
            RefreshRecord();
            RefreshDifficulty(animate);
        }

        /// <summary>
        /// 난이도 버튼 4개에 현재 곡의 레벨을 쓰고, 곡에 없는 난이도는 흐리게, 선택된 난이도는 위로 올립니다.
        /// </summary>
        private void RefreshDifficulty(bool animate)
        {
            if (musicList == null || musicList.Count == 0) return;
            if (difficultyButtonRects == null) return;

            for (int i = 0; i < DIFFICULTY_COUNT; i++)
            {
                Difficulty diff = (Difficulty)i;

                string levelText = "-";
                float alpha = unavailableDifficultyAlpha;
                if (TryGetLevel(selectedMusic, diff, out int level))
                {
                    levelText = level.ToString();
                    alpha = 1f;
                }

                SetText((Texts)((int)Texts.Level_Easy + i), levelText);

                if (difficultyButtonGroups[i] != null)
                    difficultyButtonGroups[i].alpha = alpha;

                float targetY = difficultyButtonBaseY[i];
                if (diff == selectedDifficulty)
                    targetY += selectedDifficultyRaise;

                MoveDifficultyButton(i, targetY, animate);
            }
        }

        private void MoveDifficultyButton(int index, float targetY, bool animate)
        {
            RectTransform rect = difficultyButtonRects[index];
            if (rect == null) return;

            if (difficultyButtonTweens[index] != null)
                difficultyButtonTweens[index].Kill();
            difficultyButtonTweens[index] = null;

            if (!animate)
            {
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
                return;
            }

            // 곡만 넘겨 선택이 그대로인 버튼은 트윈을 만들지 않는다
            if (Mathf.Approximately(rect.anchoredPosition.y, targetY)) return;

            difficultyButtonTweens[index] = rect.DOAnchorPosY(targetY, difficultyRaiseDuration)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject);
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

            if (userDataManager.TryGetRecord(selectedMusic.id, selectedDifficulty, out var record))
            {
                scoreText = record.bestScore.ToString("N0");
                comboText = record.bestCombo.ToString();
                rateText = $"{record.bestRate:F2}%";
            }

            // 클리어 타입은 곡 슬롯의 난이도별 별 아이콘이 보여준다
            SetText(Texts.BestScore, scoreText);
            SetText(Texts.BestCombo, comboText);
            SetText(Texts.BestRate, rateText);
        }

        /// <summary>
        /// 선택된 곡의 제목·아티스트·BPM을 정보 패널에 표시합니다. 제목·아티스트는 설정의 곡 표시 언어를 따릅니다.
        /// </summary>
        private void RefreshMusicInfo()
        {
            if (musicList == null || musicList.Count == 0) return;

            MusicSO music = selectedMusic;
            SetText(Texts.TitleText, LocalizedTextUtil.Get(music.title, music.name));
            SetText(Texts.ArtistText, LocalizedTextUtil.Get(music.producer));

            string bpmText = "BPM -";
            if (music.bpm > 0)
                bpmText = $"BPM {music.bpm}";
            SetText(Texts.BPMText, bpmText);
        }

        // 프리팹에 텍스트가 없으면(바인딩 실패) 건너뛴다
        private void SetText(Texts textType, string value)
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
        /// 세로는 각도(sin)로 정하면 가장자리로 갈수록 간격이 좁아져 패널이 겹치므로 일정한 간격으로 둡니다.
        /// </summary>
        private void LayoutSlots()
        {
            if (slotRects == null) return;

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                float p = (i - CENTER_INDEX) + wheelOffset;
                float theta = p * wheelStepAngle * Mathf.Deg2Rad;

                float x = wheelRadius * (1f - Mathf.Cos(theta));
                float y = -p * wheelSlotSpacing; // p가 클수록 아래

                // 선택 위치에 가까울수록 더 내민다(|p| = 0에서 전부, 1 이상이면 0). 회전 중에도 끊김 없이 들어가고 나온다
                float selectedWeight = Mathf.Clamp01(1f - Mathf.Abs(p));
                x -= selectedSlotOffset * selectedWeight;

                slotRects[i].anchoredPosition = new Vector2(x, y);

                slotCanvasGroups[i].alpha = GetSlotAlpha(Mathf.Abs(p));
            }
        }

        /// <summary>
        /// 보이는 9칸은 불투명, 그 바깥 버퍼 칸은 0. 회전 중에는 그 사이를 선형 보간해 버퍼 칸이 튀어나오지 않게 합니다.
        /// </summary>
        private float GetSlotAlpha(float distance)
        {
            float edgeDistance = VISIBLE_COUNT / 2;      // 4: 보이는 양 끝 칸
            float hiddenDistance = edgeDistance + 1f;    // 5: 첫 숨김 버퍼 칸

            if (distance <= edgeDistance)
                return 1f;

            if (distance <= hiddenDistance)
                return Mathf.Lerp(1f, 0f, distance - edgeDistance);

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

            if (_sfx != null)
                _sfx.Play(_wheelTickSound);
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
            // 곡 앨범아트 갱신(케이스와 LP 라벨 두 곳). 케이스 쪽 회전은 프리팹 기울기 그대로 둔다
            SetAlbumArt(Images.CaseAlbumArt);
            SetAlbumArt(Images.LPAlbumArt);

            RefreshMusicInfo();

            // 곡이 바뀌면 회전을 0도부터 다시 시작
            lpAngle = 0f;
            if (lpRect != null)
                lpRect.localRotation = Quaternion.identity;

            PlayPreviewAudio();
        }

        private void SetAlbumArt(Images target)
        {
            Image image = GetImage((int)target);
            if (image == null) return;

            image.sprite = selectedMusic.albumArt;
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

            int x = Sign(direction.x);
            int y = Sign(direction.y);

            // 위를 누른 채 좌우를 누르는 등 다른 키 때문에 값이 바뀌어 다시 온 이벤트에서는, 이미 눌려 있던 축을 또 움직이지 않는다
            int newX = x == holdDirX ? 0 : x;
            int newY = y == holdDirY ? 0 : y;

            holdDirX = x;
            holdDirY = y;
            if (newY != 0)
            {
                holdElapsed = 0f;
                nextRepeatAt = holdRepeatDelay;
            }

            if (newX != 0 || newY != 0)
                MoveSelection(newX, newY);
        }

        /// <summary>
        /// 위/아래를 누르고 있으면 holdRepeatDelay 뒤부터 연속으로 곡을 넘긴다. 간격은 holdRepeatAccelTime에 걸쳐 최소 간격까지 줄어든다.
        /// 첫 이동은 이벤트(HandleSelect)가 맡고, 여기서는 그 뒤 반복만 한다.
        /// </summary>
        private void UpdateHoldRepeat()
        {
            if (musicList == null || musicList.Count == 0 || inputManager == null) return;

            // 위에 다른 UI가 떠 있으면 눌려 있지 않은 것으로 본다
            Vector2 value = IsTopUI() ? inputManager.SelectValue : Vector2.zero;
            holdDirX = Sign(value.x);

            // 뗐거나 반대 방향으로 바뀌면 반복을 멈춘다(새로 누른 방향은 이벤트가 처리한다)
            if (Sign(value.y) != holdDirY)
            {
                holdDirY = 0;
                return;
            }
            if (holdDirY == 0) return;

            holdElapsed += Time.unscaledDeltaTime;

            // 프레임이 끊겨도 한 번에 몰아서 넘기지 않도록 프레임당 최대 2칸
            const int MAX_STEPS_PER_FRAME = 2;
            int steps = 0;
            while (holdElapsed >= nextRepeatAt)
            {
                MoveSelection(0, holdDirY);

                float accel = holdRepeatAccelTime > 0f ? Mathf.Clamp01((nextRepeatAt - holdRepeatDelay) / holdRepeatAccelTime) : 1f;
                float interval = Mathf.Max(0.01f, Mathf.Lerp(holdRepeatStartInterval, holdRepeatMinInterval, accel));
                nextRepeatAt += interval;

                if (++steps >= MAX_STEPS_PER_FRAME)
                {
                    if (holdElapsed >= nextRepeatAt)
                        nextRepeatAt = holdElapsed + interval;
                    break;
                }
            }
        }

        private static int Sign(float value)
        {
            if (value > 0f) return 1;
            if (value < 0f) return -1;
            return 0;
        }

        /// <summary>
        /// 곡(y)·난이도(x)를 한 칸 옮긴다. y = +1 위(이전 곡), -1 아래(다음 곡) / x = +1 오른쪽(어려운 쪽), -1 왼쪽
        /// </summary>
        private void MoveSelection(int x, int y)
        {
            // 상하: 곡 선택 이동 (원형 큐)
            var isMusicChanged = y != 0;
            if (y > 0)
                selectedIndex = WrapIndex(selectedIndex - 1);
            else if (y < 0)
                selectedIndex = WrapIndex(selectedIndex + 1);

            // 고른 난이도가 새 곡에 없으면 가까운 난이도로 옮기고, 있는 곡으로 돌아오면 원래 난이도로 되돌린다
            if (isMusicChanged)
                selectedDifficulty = ResolveDifficulty(selectedMusic, preferredDifficulty);

            // 좌우: 난이도 선택 (level -1인 난이도는 스킵). 직접 고른 값은 다음 곡에서도 기억한다
            // 곡 이동에 따른 자동 보정은 효과음 대상이 아니므로 비교 기준을 여기서 잡는다
            Difficulty difficultyBefore = selectedDifficulty;
            if (x > 0)
            {
                for (Difficulty d = selectedDifficulty + 1; d <= Difficulty.Extreme; d++)
                    if (IsAvailable(selectedMusic, d)) { selectedDifficulty = d; preferredDifficulty = d; break; }
            }
            else if (x < 0)
            {
                for (Difficulty d = selectedDifficulty - 1; d >= Difficulty.Easy; d--)
                    if (IsAvailable(selectedMusic, d)) { selectedDifficulty = d; preferredDifficulty = d; break; }
            }

            // 끝이라 더 못 가면(선택이 그대로면) 소리 없음
            if (selectedDifficulty != difficultyBefore && _sfx != null)
                _sfx.Play(_difficultyChangeSound);

            RefreshList(true);

            // 상하 이동은 바퀴를 한 칸 돌린다(아래 = 다음 곡 = 리스트가 위로)
            if (y > 0)
                StartWheelTick(-1);
            else if (y < 0)
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
            return TryGetLevel(music, diff, out _);
        }

        // 레벨 -1은 "이 난이도는 아직 준비되지 않음"을 뜻한다
        private bool TryGetLevel(MusicSO music, Difficulty diff, out int level)
        {
            level = -1;
            return music.level != null && music.level.TryGetValue(diff, out level) && level != -1;
        }

        /// <summary>
        /// 고른 난이도가 곡에 있으면 그대로, 없으면 가장 가까운 준비된 난이도를 돌려줍니다. 거리가 같으면 쉬운 쪽을 먼저 봅니다.
        /// 전부 없으면 고른 난이도를 그대로 둡니다(시작은 HandleSubmit의 IsAvailable이 막는다).
        /// </summary>
        private Difficulty ResolveDifficulty(MusicSO music, Difficulty preferred)
        {
            if (IsAvailable(music, preferred)) return preferred;

            for (int distance = 1; distance < DIFFICULTY_COUNT; distance++)
            {
                Difficulty lower = preferred - distance;
                if (lower >= Difficulty.Easy && IsAvailable(music, lower)) return lower;

                Difficulty higher = preferred + distance;
                if (higher <= Difficulty.Extreme && IsAvailable(music, higher)) return higher;
            }

            return preferred;
        }

        protected override void HandleCancel()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
        }
    }
}
