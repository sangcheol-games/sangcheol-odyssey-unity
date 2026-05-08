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
        private const float HoverScale = 1.1f;
        private const float HoverDuration = 0.15f;

        private Vector3 _adventureBaseScale = Vector3.one;
        private Coroutine _adventureScaleRoutine;

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
        }

        private void Init()
        {
            BindButton(typeof(Buttons));
            BindImage(typeof(Images));

            GameObject adventureGo = GetButton((int)Buttons.Adventure).gameObject;

            BindEvent(adventureGo, EventTriggerType.PointerClick, OnClickAdventure);
            BindEvent(adventureGo, EventTriggerType.PointerEnter, OnPointerEnterAdventure);
            BindEvent(adventureGo, EventTriggerType.PointerExit, OnPointerExitAdventure);
            
            //BindEvent(GetButton((int)Buttons.Lounge).gameObject, EventTriggerType.PointerClick, OnClickLounge);
            BindEvent(GetButton((int)Buttons.Setting).gameObject, EventTriggerType.PointerClick, OnClickSetting);
            BindEvent(GetButton((int)Buttons.Quit).gameObject, EventTriggerType.PointerClick, ExitGame);
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

        private void OnPointerEnterAdventure()
        {
            StartAdventureScale(_adventureBaseScale * HoverScale);
        }

        private void OnPointerExitAdventure()
        {
            StartAdventureScale(_adventureBaseScale);
        }

        private void StartAdventureScale(Vector3 toScale)
        {
            if (_adventureScaleRoutine != null) StopCoroutine(_adventureScaleRoutine);
            _adventureScaleRoutine = StartCoroutine(ScaleTo(GetButton((int)Buttons.Adventure).transform, toScale, HoverDuration));
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
