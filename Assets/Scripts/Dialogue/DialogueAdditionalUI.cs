using UnityEngine;
using UnityEngine.UI;
using PixelCrushers.DialogueSystem;

namespace SCOdyssey.Dialogue
{
    // 뭐여이건 어따달라고 만든거지?
    public class DialogueAdditionalUI : MonoBehaviour
    {
        public Button autoPlay;
        public Button backLog;
        public Button hideUI;
        public Button skip;

        public CanvasGroup backLogPanel;

        public Button showUI;
        public CanvasGroup dialoguePanel;
        public CanvasGroup topMenuPanel;

        public Transform skipAlertPanel;
        public Button approveSkip;

        private bool isAuto = false;


        private void OnEnable()
        {
            autoPlay.onClick.AddListener(OnAutoPlayTriggered);
            backLog.onClick.AddListener(OnBackLogTriggered);
            hideUI.onClick.AddListener(OnHideUITriggered);
            skip.onClick.AddListener(OnSkipTriggered);

            showUI.onClick.AddListener(OnApproveSkipTriggered);
            approveSkip.onClick.AddListener(OnApproveSkipTriggered);
        }

        private void OnDisable()
        {
            autoPlay.onClick.RemoveAllListeners();
            backLog.onClick.RemoveAllListeners();
            hideUI.onClick.RemoveAllListeners();
            skip.onClick.RemoveAllListeners();

            showUI.onClick.RemoveAllListeners();
            approveSkip.onClick.RemoveAllListeners();
        }

        private void OnAutoPlayTriggered()
        {
            if (isAuto)
            {
                DialogueManager.displaySettings.subtitleSettings.continueButton
                    = DisplaySettings.SubtitleSettings.ContinueButtonMode.Never;
            }
            else
            {
                DialogueManager.displaySettings.subtitleSettings.continueButton
                    = DisplaySettings.SubtitleSettings.ContinueButtonMode.Always;
            }

            isAuto = !isAuto;
        }

        private void OnBackLogTriggered()
        {
            backLogPanel.alpha = 1f;
            backLogPanel.interactable = true;
            backLogPanel.blocksRaycasts = true;
        }

        private void OnHideUITriggered()
        {
            if (!showUI.isActiveAndEnabled)
            {
                dialoguePanel.alpha = 0f;
                dialoguePanel.interactable = false;
                dialoguePanel.blocksRaycasts = false;

                topMenuPanel.alpha = 0f;
                topMenuPanel.interactable = false;
                topMenuPanel.blocksRaycasts = false;

                showUI.gameObject.SetActive(true);
            }
            else
            {
                dialoguePanel.alpha = 1f;
                dialoguePanel.interactable = true;
                dialoguePanel.blocksRaycasts = true;

                topMenuPanel.alpha = 1f;
                topMenuPanel.interactable = true;
                topMenuPanel.blocksRaycasts = true;

                showUI.gameObject.SetActive(false);
            }
        }

        private void OnSkipTriggered()
        {
            
        }

        private void OnApproveSkipTriggered()
        {
            DialogueManager.StopAllConversations();
        }
    }
}