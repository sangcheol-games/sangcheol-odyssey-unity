using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.Domain.Entity;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.UI
{
    /// <summary>
    /// 곡 진입 로딩 화면. GameDataLoader가 GameScene 안에서 PushMode.Overlay로 띄우고 직접 주도한다.
    /// PauseUI와 같은 경로라 UIManager가 캔버스 설정(ScreenSpaceCamera·sortingOrder 100+)과
    /// 16:9 레터박스를 알아서 처리해 준다.
    ///
    /// 입력 처리를 하지 않으므로 MusicListUI처럼 OnEnable/OnDisable을 비워 둔다.
    /// BaseUI.OnEnable의 SwitchToUI()가 GameManager.Start()의 SwitchToGameplay()와 경합하는데,
    /// 둘 다 기본 실행 순서라 어느 쪽이 나중일지 보장이 없기 때문이다.
    /// 대신 BeginLoading/Close가 IInputManager.SetInputActive를 짝으로 토글한다 — 플래그라 실행 순서와 무관하고,
    /// 성공·실패 어느 경로로 닫혀도 입력이 반드시 되살아난다.
    ///
    /// 비동기는 UniTask로 통일한다. 이 클래스의 대기는 전부 호출자가 완료를 기다리는 종류라
    /// 프로젝트의 코루틴 선례(EffectController·ShowClearSequence 등 fire-and-forget 연출)와 성격이 다르다.
    ///
    /// 바인딩 주의: BaseUI.FindChild는 GetComponentsInChildren을 includeInactive 없이 호출하므로
    /// 아래 enum이 가리키는 자식은 프리팹에서 전부 활성이어야 한다.
    /// </summary>
    public class GameLoadingUI : BaseUI
    {
        // 진행률이 연속적으로 보이게 하는 속도.
        private const float BarFollowSpeed = 1.6f;

        // 실패 안내를 읽을 시간.
        private const float ErrorDwellSeconds = 1.5f;

        // 난이도별 스프라이트. Difficulty enum 순서(Easy, Normal, Hard, Extreme)와 인덱스가 일치해야 함.
        // 배지와 LP 둘레 호는 같은 난이도 색이어야 하므로 두 배열의 같은 인덱스는 같은 색으로 맞춘다.
        [SerializeField] private Sprite[] difficultyBadgeSprites;
        [SerializeField] private Sprite[] difficultyCircularSprites;

        private enum Texts
        {
            MusicTitleText,   // 곡 제목
            ArtistText,       // 아티스트
            LevelText,        // 난이도 레벨
            StatusText,       // 진행 상태 문구
            IllustText,       // 일러스트레이터
            MVText,           // MV(영상) 제작자
            BPMText           // BPM
        }

        private enum Images
        {
            AlbumArt,           // 앨범 아트
            ProgressBar,        // 진행률 바 배경 (실패 시 통째로 숨긴다)
            ProgressFill,       // 진행률 바 채움
            ProgressIcon,       // 채움 끝을 따라 움직이는 아이콘
            DifficultyBadge,    // 난이도 배지 (난이도별 스프라이트 교체)
            DifficultyCircular, // LP 둘레 호 (배지와 같은 난이도 색)
            BackgroundArt       // 곡 배경 아트 (없으면 숨긴다)
        }

        private CanvasGroup canvasGroup;

        private float _targetProgress;
        private float _shownProgress;

        protected override void Awake()
        {
            base.Awake();

            BindText(typeof(Texts));
            BindImage(typeof(Images));

            canvasGroup = GetComponent<CanvasGroup>();  // 루트 컴포넌트라서 Bind대신 GetComponent 사용
        }

        private void Update()
        {
            if (Mathf.Approximately(_shownProgress, _targetProgress)) return;

            _shownProgress = Mathf.MoveTowards(_shownProgress, _targetProgress, BarFollowSpeed * UnscaledStep());
            ApplyProgress(_shownProgress);
        }

        /// <summary>
        /// 채움과 아이콘을 함께 갱신한다.
        /// fillAmount는 RectTransform을 바꾸지 않으므로(그리는 양만 바뀐다) 아이콘은 앵커로 직접 옮겨야 한다.
        /// 앵커 비율로 옮기면 바 폭을 읽지 않아도 되고 해상도가 바뀌어도 채움 끝에 붙어 있다.
        /// </summary>
        private void ApplyProgress(float value)
        {
            // 바인딩이 어긋났을 때 매 프레임 예외가 쏟아지는 것만은 막는다.
            Image fill = GetImage((int)Images.ProgressFill);
            if (fill != null) fill.fillAmount = value;

            Image icon = GetImage((int)Images.ProgressIcon);
            if (icon == null) return;

            // y는 인스펙터에서 잡은 세로 정렬을 그대로 둔다.
            RectTransform rect = icon.rectTransform;
            rect.anchorMin = new Vector2(value, rect.anchorMin.y);
            rect.anchorMax = new Vector2(value, rect.anchorMax.y);
        }

        /// <summary>
        /// 난이도에 맞는 스프라이트로 교체한다(ResultUI의 등급 도장과 같은 방식).
        /// 로딩 화면은 던지면 안 되므로, 배열이 덜 채워졌으면 경고만 남기고 지금 그림을 유지한다.
        /// </summary>
        private void SetDifficultySprite(Images slot, Sprite[] sprites, Difficulty difficulty)
        {
            Image image = GetImage((int)slot);
            if (image == null) return;

            int index = (int)difficulty;
            if (sprites == null || index >= sprites.Length || sprites[index] == null)
            {
                Debug.LogWarning($"[GameLoadingUI] {slot}에 {difficulty} 스프라이트가 지정되지 않았습니다.");
                return;
            }

            image.sprite = sprites[index];
        }

        // ── GameDataLoader가 부르는 API ──────────────────────────

        /// <summary>
        /// 곡 정보를 표시하고 화면을 불투명하게 만든다.
        /// UIManager가 인스턴스를 영구 캐시해 Awake가 생애 1회만 돌므로, 매 표시마다 상태를 여기서 전부 되돌린다.
        /// </summary>
        public void BeginLoading(MusicSO music, Difficulty difficulty)
        {
            // 입력 차단/복구는 이 클래스가 짝으로 소유한다(Close에서 되살린다).
            // 호출자가 복구를 맡으면 실패 경로에서 누락돼 로비에서 조작이 죽는다.
            // 입력 맵은 건드리지 않는다 — GameManager.Start의 SwitchToGameplay와 경합하지 않도록.
            SetInputActive(false);

            // 지난 페이드아웃으로 0이 돼 있다. UIManager가 인스턴스를 영구 캐시하므로 매번 되돌려야 한다.
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            _targetProgress = 0f;
            _shownProgress = 0f;

            // 지난 실패에서 숨겼을 수 있으므로 매번 되살린다.
            GetImage((int)Images.ProgressBar).gameObject.SetActive(true);

            // Update는 목표값에 도달해 있으면 조기 반환하므로 여기서 직접 되돌려야 한다.
            // 안 그러면 아이콘이 지난 로딩이 끝난 위치(100%)에 남는다.
            ApplyProgress(0f);
            GetText((int)Texts.StatusText).text = "불러오는 중...";

            // 곡 정보와 무관하므로 music이 없어도 맞춰 둔다. 인스턴스가 캐시돼 지난 곡의 난이도 색이 남아 있다.
            SetDifficultySprite(Images.DifficultyBadge, difficultyBadgeSprites, difficulty);
            SetDifficultySprite(Images.DifficultyCircular, difficultyCircularSprites, difficulty);

            if (music == null) return;

            GetImage((int)Images.AlbumArt).sprite = music.albumArt;

            // sprite가 null인 Image는 흰 사각형으로 그려지므로, 배경 아트가 없는 곡은 끄고 아래 Background를 보인다.
            Image backgroundArt = GetImage((int)Images.BackgroundArt);
            backgroundArt.sprite = music.backgroundArt;
            backgroundArt.enabled = music.backgroundArt != null;
            GetText((int)Texts.MusicTitleText).text = GetLocalizedText(music.title, music.name);
            GetText((int)Texts.ArtistText).text = GetLocalizedText(music.producer);

            // 비어 있는 정보는 레벨과 같은 규칙으로 "-"를 보인다. 지우지 않으면 지난 곡 값이 남는다.
            GetText((int)Texts.IllustText).text = OrDash(music.illustrator);
            GetText((int)Texts.MVText).text = OrDash(music.animator);
            GetText((int)Texts.BPMText).text = music.bpm > 0 ? music.bpm.ToString() : "-";

            // 레벨 -1은 "이 난이도는 아직 준비되지 않음"을 뜻한다(AdventureUI.IsAvailable과 같은 규칙).
            string levelLabel = "-";
            if (music.level != null && music.level.TryGetValue(difficulty, out int level) && level != -1)
            {
                levelLabel = level.ToString();
            }
            GetText((int)Texts.LevelText).text = levelLabel;
        }

        /// <summary>진행률 목표값과 문구를 갱신한다. 실제 바는 Update가 따라간다.</summary>
        public void SetProgress(float fraction01, string label)
        {
            _targetProgress = Mathf.Clamp01(fraction01);
            if (!string.IsNullOrEmpty(label)) GetText((int)Texts.StatusText).text = label;
        }

        /// <summary>페이드아웃 후 스택에서 내린다. 취소되더라도 Close는 보장된다.</summary>
        public async UniTask FadeOutAndCloseAsync(float seconds, CancellationToken ct)
        {
            try
            {
                await FadeAsync(1f, 0f, seconds, ct);
            }
            finally
            {
                Close();
            }
        }

        /// <summary>
        /// 로딩 실패. 사유를 보여준 뒤 로비로 돌아가고 스스로 닫는다.
        /// @UI_Root가 DontDestroyOnLoad라 이 작업은 씬 전환을 넘어 살아남으므로 복귀 전환까지 덮는다.
        /// (PauseUI.OnClickQuitButton도 UI에서 직접 LoadScene을 부른다)
        /// </summary>
        public void FailToLobby(string message)
        {
            // 호출자(GameDataLoader)는 이걸 부른 뒤 곧장 끝나므로 기다리지 않는다.
            FailToLobbyAsync(message).Forget();
        }

        private async UniTaskVoid FailToLobbyAsync(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                message = "곡을 불러오지 못했습니다.";
            }

            Debug.LogWarning("[GameLoadingUI] 곡을 시작하지 못해 로비로 돌아갑니다: " + message);

            CancellationToken ct = this.GetCancellationTokenOnDestroy();

            // 여기만 null을 확인한다. 바인딩이 어긋나 위쪽(BeginLoading)에서 던져도
            // 이 복귀 경로만은 끝까지 완주해야 플레이어가 화면에 갇히지 않는다.
            // 배경까지 함께 끈다(ProgressFill이 자식이라 같이 사라진다).
            Image bar = GetImage((int)Images.ProgressBar);
            if (bar != null) bar.gameObject.SetActive(false);

            TMP_Text status = GetText((int)Texts.StatusText);
            if (status != null) status.text = message;

            await DelayRealtime(ErrorDwellSeconds, ct);

            SceneManager.LoadScene("MainScene");
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 씬 전환은 프레임 끝에 일어난다. 한 프레임 넘겨 로비가 올라온 뒤 걷는다.

            await FadeOutAndCloseAsync(0.3f, ct);
        }

        // ── 내부 ─────────────────────────────────────────────────

        private void Close()
        {
            SetInputActive(true);
            // 활성 상태는 UIManager.RefreshVisibility가 소유한다. 직접 SetActive하지 않는다.
            if (ServiceLocator.TryGet<IUIManager>(out var uiManager)) uiManager.CloseUI(this);
        }

        private static void SetInputActive(bool active)
        {
            if (ServiceLocator.TryGet<IInputManager>(out var input)) input.SetInputActive(active);
        }

        private async UniTask FadeAsync(float from, float to, float seconds, CancellationToken ct)
        {
            if (canvasGroup == null) return;

            canvasGroup.alpha = from;

            if (seconds <= 0f)
            {
                canvasGroup.alpha = to;
                return;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += UnscaledStep();
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / seconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            canvasGroup.alpha = to;
        }

        private static UniTask DelayRealtime(float seconds, CancellationToken ct)
        {
            return UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime, cancellationToken: ct);
        }

        /// <summary>
        /// 로딩 중에는 씬 활성화·동기 파싱·FMOD createSound 때문에 1초짜리 프레임이 실제로 나온다.
        /// 클램프하지 않으면 알파가 한 프레임에 점프해 페이드가 아니라 컷이 된다.
        /// (최소 표시 시간은 이 값이 아니라 Time.realtimeSinceStartup으로 재야 한다 — GameDataLoader 참조)
        /// </summary>
        private static float UnscaledStep()
        {
            return Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        }

        private static string OrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        /// <summary>MusicListUI.GetLocalizedText와 같은 규칙. 로딩 화면은 절대 던지면 안 되므로 폴백을 둔다.</summary>
        private static string GetLocalizedText(LocalizedString localizedString, string fallback = "")
        {
            if (localizedString == null) return fallback;

            try
            {
                if (!ServiceLocator.TryGet<ISettingsManager>(out var settings))
                    return localizedString.GetLocalizedString();

                var locale = LocalizationSettings.AvailableLocales.GetLocale(settings.Current.displayLanguageCode);
                if (locale == null) return localizedString.GetLocalizedString();

                return LocalizationSettings.StringDatabase
                    .GetLocalizedStringAsync(localizedString.TableReference, localizedString.TableEntryReference, locale)
                    .WaitForCompletion();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameLoadingUI] 곡 이름을 가져오지 못했습니다: " + e.Message);
                return fallback;
            }
        }

        
        protected override void OnEnable() { }
        protected override void OnDisable() { }
        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() { }
        protected override void HandleCancel() { }

    }
}
