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

            BindEvent(GetButton((int)Buttons.Lounge).gameObject, EventTriggerType.PointerClick, OnClickLounge);


            BindEvent(GetButton((int)Buttons.Adventure).gameObject, EventTriggerType.PointerEnter, OnClickAdventure);


        }

        private void OnClickAdventure()
        {
            Debug.Log("OnClickAdventure");
        }
        private void OnClickOnline()
        {
            Debug.Log("OnClickOnline");
        }
        private void OnClickLounge()
        {
            Debug.Log("OnClickLounge");
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            GetImage((int)Images.Profile).color = Color.red;
        }
        private void OnClickSetting()
        {
            Debug.Log("OnClickSetting");
        }
    }
}
