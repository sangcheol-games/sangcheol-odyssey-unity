using UnityEngine;
using SCOdyssey.App;
using SCOdyssey.UI;
using UnityEngine.EventSystems;
using SCOdyssey.Core;

namespace SCOdyssey
{
    public class MainUI : BaseUI
    {
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

        private void Start()
        {
            Init();
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
