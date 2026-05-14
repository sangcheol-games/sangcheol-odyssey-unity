using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SCOdyssey.App;
using SCOdyssey.UI;
using UnityEngine.EventSystems;
using SCOdyssey.Core;

namespace SCOdyssey
{
    public class MainUI : BaseUI
    {
        private const float HoverScale = 1.1f;
        private const float HoverDuration = 0.15f;

        private readonly Dictionary<Transform, Coroutine> _hoverRoutines = new();

        private enum Buttons
        {
            Adventure,
            Online,
            Lounge,
            Setting,
            Quit
        }

        private enum Images
        {
            Profile
        }

        private void Start()
        {
            Init();

            StartCoroutine(PlayLobbyAudio());
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            BindImage(typeof(Images));

            GameObject adventureGo = GetButton((int)Buttons.Adventure).gameObject;
            GameObject settingGo = GetButton((int)Buttons.Setting).gameObject;
            GameObject quitGo = GetButton((int)Buttons.Quit).gameObject;

            BindEvent(adventureGo, EventTriggerType.PointerClick, OnClickAdventure);
            //BindEvent(GetButton((int)Buttons.Lounge).gameObject, EventTriggerType.PointerClick, OnClickLounge);
            BindEvent(settingGo, EventTriggerType.PointerClick, OnClickSetting);
            BindEvent(quitGo, EventTriggerType.PointerClick, ExitGame);

            BindHoverScale(adventureGo);
            BindHoverScale(settingGo);
            BindHoverScale(quitGo);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            StartCoroutine(PlayLobbyAudio());
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            StopLobbyAudio();
        }

        private IEnumerator PlayLobbyAudio()
        {
            if(!ServiceLocator.TryGet<IAudioManager>(out var audioManager))
            {
                yield break;
            }

            if(audioManager.IsPlaying) audioManager.Stop();

            var audioFilePath = "Lobby BGM.wav";

            audioManager.LoadAudio(audioFilePath, loopHint: true);
            while(!audioManager.IsLoaded) yield return null;

            var dspStartTime = audioManager.GetDSPTime();
            audioManager.PlayScheduled(dspStartTime, loopPlay: true);
        }

        private void StopLobbyAudio()
        {
            var audioManager = ServiceLocator.Get<IAudioManager>();
            if(audioManager.IsPlaying) audioManager.Stop();
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
            var audioManager = ServiceLocator.Get<IAudioManager>();
            audioManager.PlaySound("ui_button_simple_click_06.wav");
            ServiceLocator.Get<IUIManager>().ShowUI<GameSettingUI>();
        }

        private void BindHoverScale(GameObject go)
        {
            Transform target = go.transform;
            BindEvent(go, EventTriggerType.PointerEnter, () => StartHoverScale(target, Vector3.one * HoverScale));
            BindEvent(go, EventTriggerType.PointerExit, () => StartHoverScale(target, Vector3.one));
        }

        private void StartHoverScale(Transform target, Vector3 toScale)
        {
            if (_hoverRoutines.TryGetValue(target, out Coroutine running) && running != null)
                StopCoroutine(running);
            _hoverRoutines[target] = StartCoroutine(ScaleTo(target, toScale, HoverDuration));
        }

        private IEnumerator ScaleTo(Transform target, Vector3 toScale, float duration)
        {
            Vector3 from = target.localScale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                target.localScale = Vector3.Lerp(from, toScale, t / duration);
                yield return null;
            }
            target.localScale = toScale;
        }


        private void ExitGame()
        {
            var audioManager = ServiceLocator.Get<IAudioManager>();
            audioManager.PlaySound("ui_button_simple_click_06.wav");
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.ShowUI<ExitGameUI>();
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
            ExitGame();
        }
    }
}
