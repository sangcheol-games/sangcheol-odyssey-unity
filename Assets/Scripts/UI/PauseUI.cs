using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SCOdyssey.UI
{
    public class PauseUI : BaseUI
    {
        private enum Buttons
        {
            Btn_Resume,
            Btn_Retry,
            Btn_Quit
        }


        protected override void Awake()
        {
            base.Awake();

            BindButton(typeof(Buttons));

            GetButton((int)Buttons.Btn_Resume).onClick.AddListener(OnClickResumeButton);
            GetButton((int)Buttons.Btn_Retry).onClick.AddListener(OnClickRetryButton);
            GetButton((int)Buttons.Btn_Quit).onClick.AddListener(OnClickQuitButton);
        }

        private void OnClickResumeButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            ServiceLocator.Get<IGameManager>().Resume();
        }

        private void OnClickRetryButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            SceneManager.LoadScene("GameScene");
        }

        private void OnClickQuitButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            SceneManager.LoadScene("MainScene");
        }

        protected override void HandleSelect(Vector2 dir) { }
        protected override void HandleSubmit() => OnClickResumeButton();
        protected override void HandleCancel() => OnClickResumeButton(); // ESC로도 재개
    }
}
