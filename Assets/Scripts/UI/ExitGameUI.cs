using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;


namespace SCOdyssey.UI
{
    public class ExitGameUI : BaseUI
    {
        private enum Buttons
        {
            Confirm,
            Cancel,
            Background
        }

        protected override void Awake()
        {
            base.Awake();

            BindButton(typeof(Buttons));

            GetButton((int)Buttons.Confirm).onClick.AddListener(ExitGame);
            GetButton((int)Buttons.Cancel).onClick.AddListener(CloseUI);
            GetButton((int)Buttons.Background).onClick.AddListener(CloseUI);
        }

        private void CloseUI()
        {
            var uiManager = ServiceLocator.Get<IUIManager>();
            uiManager.CloseUI(this);
        }

        private void ExitGame()
        {
            Application.Quit();
        }

        protected override void HandleSelect(Vector2 direction) { }

        protected override void HandleSubmit()
        {
            ExitGame();
        }

        protected override void HandleCancel()
        {
            CloseUI();
        }
    }

}
