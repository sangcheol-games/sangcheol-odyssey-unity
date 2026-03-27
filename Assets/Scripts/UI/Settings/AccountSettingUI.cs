using SCOdyssey.App;
using SCOdyssey.Core;
using SCOdyssey.UI;
using UnityEngine;

namespace SCOdyssey
{
    public class AccountSettingUI : BaseUI
    {
        private enum Buttons
        {
            Tab_Game,
            Tab_Graphic,
            Tab_Sound,
            Tab_Account,
            Btn_Close
        }

        private void Start()
        {
            Init();
        }

        private void Init()
        {
            BindButton(typeof(Buttons));

            GetButton((int)Buttons.Tab_Game).onClick.AddListener(SwitchToGame);
            GetButton((int)Buttons.Tab_Graphic).onClick.AddListener(SwitchToGraphic);
            GetButton((int)Buttons.Tab_Sound).onClick.AddListener(SwitchToSound);
            GetButton((int)Buttons.Btn_Close).onClick.AddListener(OnClickClose);
        }

        private void SwitchToGame()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<GameSettingUI>();
        }

        private void SwitchToGraphic()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<GraphicSettingUI>();
        }

        private void SwitchToSound()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
            uiManager.ShowUI<SoundSettingUI>();
        }

        private void OnClickClose()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
        }

        protected override void HandleSelect(Vector2 direction) { }
        protected override void HandleSubmit() { }
        protected override void HandleCancel() => OnClickClose();
    }
}
