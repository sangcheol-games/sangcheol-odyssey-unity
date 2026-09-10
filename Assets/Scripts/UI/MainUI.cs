using System.Collections;
using UnityEngine;
using SCOdyssey.App;
using SCOdyssey.UI;
using UnityEngine.EventSystems;
using SCOdyssey.Core;

namespace SCOdyssey
{
    public class MainUI : BaseUI
    {
        // StreamingAssets/Music/ 기준 파일명. 로비 BGM 교체 시 프리팹 인스펙터에서 변경.
        [SerializeField] private string bgmFileName = "Lobby BGM.wav";

        private Coroutine bgmRoutine;

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
            bgmRoutine = StartCoroutine(PlayBgm());
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            // 다른 UI/씬으로 이동하면 BGM 정지 (로딩 중이었다면 대기 코루틴도 취소)
            if (bgmRoutine != null)
            {
                StopCoroutine(bgmRoutine);
                bgmRoutine = null;
            }

            if (ServiceLocator.TryGet<IAudioManager>(out var audioManager))
            {
                audioManager.Stop();
            }
        }

        private IEnumerator PlayBgm()
        {
            if (string.IsNullOrEmpty(bgmFileName))
            {
                Debug.LogWarning("[MainUI] bgmFileName is empty!");
                bgmRoutine = null;
                yield break;
            }

            if (!ServiceLocator.TryGet<IAudioManager>(out var audioManager))
            {
                Debug.LogError("[MainUI] IAudioManager not found in ServiceLocator!");
                bgmRoutine = null;
                yield break;
            }

            if (audioManager.IsPlaying) audioManager.Stop();

            audioManager.LoadAudio(bgmFileName, loopHint: true);
            // NONBLOCKING 로드 완료까지 대기 (보통 1-3프레임)
            while (!audioManager.IsLoaded) yield return null;

            audioManager.PlayScheduled(audioManager.GetDSPTime(), loopPlay: true);
            bgmRoutine = null;
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
