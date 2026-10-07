using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using SCOdyssey.App;
using SCOdyssey.Audio;
using SCOdyssey.UI;
using UnityEngine.EventSystems;
using SCOdyssey.Core;

namespace SCOdyssey
{
    public class MainUI : BaseUI
    {
        // StreamingAssets/Music/ 기준 파일명. 로비 BGM 교체 시 프리팹 인스펙터에서 변경.
        [SerializeField] private string bgmFileName = "Lobby BGM.wav";

        private CancellationTokenSource _bgmCts;

        // 부팅 때 저장한 출력 구성으로 열지 못했으면 로비에 처음 들어올 때 한 번만 알린다.
        private static bool s_audioNoticeShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAudioNotice()
        {
            s_audioNoticeShown = false;
        }

        private enum Buttons
        {
            Adventure,
            Online,
            Lounge,
            Setting
        }

        private enum Images
        {
            Profile
        }

        protected override void Awake()
        {
            base.Awake();
            Init();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            // MainUI가 표시될 때마다 BGM을 처음부터 재생
            PlayBgm();
            NotifyAudioFallbackOnce();
        }

        // TODO: 공용 알림 UI가 생기면 화면에 띄운다. 지금은 로그로만 남긴다.
        private static void NotifyAudioFallbackOnce()
        {
            if (s_audioNoticeShown) return;
            s_audioNoticeShown = true;

            if (!ServiceLocator.TryGet<IAudioEngine>(out var engine) || engine.Status == EngineStatus.Failed || engine.Status == EngineStatus.Degraded)
            {
                Debug.LogWarning(AudioUiText.BootUnavailableNotice);
                return;
            }
            if (!engine.IsRequestedConfig) Debug.LogWarning(AudioUiText.BootFallbackNotice);
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // 다른 UI/씬으로 이동하면 BGM 정지 (로딩 중이었다면 요청도 취소)
            CancelBgm();
            if (ServiceLocator.TryGet<IMusicPlayers>(out var music)) music.Lobby.Stop();
        }

        private void PlayBgm()
        {
            if (string.IsNullOrEmpty(bgmFileName))
            {
                Debug.LogWarning("[MainUI] bgmFileName is empty!");
                return;
            }

            if (!ServiceLocator.TryGet<IMusicPlayers>(out var music))
            {
                Debug.LogWarning("[MainUI] IMusicPlayers를 찾지 못해 로비 BGM을 재생하지 않습니다.");
                return;
            }

            CancelBgm();
            _bgmCts = new CancellationTokenSource();
            PlayBgmAsync(music.Lobby, bgmFileName, _bgmCts.Token).Forget();
        }

        // 로비 재생기는 마지막 요청만 유효하다(앞선 요청은 Superseded로 끝난다).
        private static async UniTaskVoid PlayBgmAsync(IMusicPlayer lobby, string fileName, CancellationToken ct)
        {
            AudioLoadResult result = await lobby.PlayAsync(fileName, true, ct);
            if (result.Status == AudioLoadStatus.NotFound || result.Status == AudioLoadStatus.DecodeError || result.Status == AudioLoadStatus.Timeout)
            {
                Debug.LogWarning("[MainUI] 로비 BGM을 재생하지 못했습니다(" + result.Status + "): " + result.Detail);
            }
        }

        private void CancelBgm()
        {
            if (_bgmCts == null) return;
            _bgmCts.Cancel();
            _bgmCts.Dispose();
            _bgmCts = null;
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            BindImage(typeof(Images));

            BindEvent(GetButton((int)Buttons.Adventure).gameObject, EventTriggerType.PointerClick, OnClickAdventure);
            BindEvent(GetButton((int)Buttons.Lounge).gameObject, EventTriggerType.PointerClick, OnClickLounge);
            BindEvent(GetButton((int)Buttons.Setting).gameObject, EventTriggerType.PointerClick, OnClickSetting);
        }

        private void OnClickAdventure()
        {
            Debug.Log("OnClickAdventure");
            ServiceLocator.Get<IUIManager>().ShowUI<AdventureUI>();
        }
        private void OnClickOnline()
        {
            Debug.Log("OnClickOnline");
        }
        private void OnClickLounge()
        {
            Debug.Log("OnClickLounge");
        }
        private void OnClickSetting()
        {
            ServiceLocator.Get<IUIManager>().ShowUI<GameSettingUI>();
        }

        protected override void HandleSelect(Vector2 direction)
        {
            Debug.Log("HandleSelect in MainUI");
        }

        protected override void HandleSubmit()
        {
            Debug.Log("HandleSubmit in MainUI");
        }

        protected override void HandleCancel()
        {
            Debug.Log("HandleCancel in MainUI");
        }
    }
}
