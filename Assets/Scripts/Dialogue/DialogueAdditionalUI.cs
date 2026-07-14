using UnityEngine;
using UnityEngine.UI;
using PixelCrushers.DialogueSystem;


namespace SCOdyssey.Dialogue
{
    public class DialogueAdditionalUI : MonoBehaviour
    {
        [Header("Buttons")]
        public Button autoPlay;
        public Button backLog;
        public Button hideUI;
        public Button skip;

        [Header("BackLog")]
        public CanvasGroup backLogPanel;
        public Button backLogClose;
        public Button backLogCloseBg;

        [Header("HideUI")]
        public CanvasGroup dialoguePanel;
        public CanvasGroup topMenuPanel;
        public Button showUI;

        [Header("Skip")]
        public CanvasGroup skipAlertPanel;
        public Button cancelSkip;
        public Button cancelSkipBg;
        public Button approveSkip;


        private bool isAuto;


        private void OnEnable()
        {
            // 기본값 세팅
            DialogueManager.displaySettings.subtitleSettings.subtitleCharsPerSecond = 40;
            DialogueManager.displaySettings.subtitleSettings.minSubtitleSeconds = 3;


            isAuto = false;

            backLogPanel.alpha = 0f;
            backLogPanel.interactable = false;
            backLogPanel.blocksRaycasts = false;

            skipAlertPanel.alpha = 0f;
            skipAlertPanel.interactable = false;
            skipAlertPanel.blocksRaycasts = false;


            autoPlay.onClick.AddListener(OnAutoPlayTriggered);
            backLog.onClick.AddListener(OnBackLogTriggered);
            hideUI.onClick.AddListener(OnHideUITriggered);
            skip.onClick.AddListener(OnSkipTriggered);

            backLogClose.onClick.AddListener(OnBackLogCloseTriggered);
            backLogCloseBg.onClick.AddListener(OnBackLogCloseTriggered);    // 투명버튼

            showUI.onClick.AddListener(OnHideUITriggered);      // 투명버튼

            cancelSkip.onClick.AddListener(OnCancelSkipTriggered);
            cancelSkipBg.onClick.AddListener(OnCancelSkipTriggered);    // 투명버튼
            approveSkip.onClick.AddListener(OnApproveSkipTriggered);
        }

        private void OnDisable()
        {
            autoPlay.onClick.RemoveAllListeners();
            backLog.onClick.RemoveAllListeners();
            hideUI.onClick.RemoveAllListeners();
            skip.onClick.RemoveAllListeners();

            backLogClose.onClick.RemoveAllListeners();
            showUI.onClick.RemoveAllListeners();
            cancelSkip.onClick.RemoveAllListeners();
            approveSkip.onClick.RemoveAllListeners();
        }


        private void OnAutoPlayTriggered()
        {
            isAuto = !isAuto;

            if (isAuto)
            {
                DialogueManager.displaySettings.subtitleSettings.continueButton
                    = DisplaySettings.SubtitleSettings.ContinueButtonMode.Never;

                if (DialogueManager.isConversationActive)
                    (DialogueManager.dialogueUI as StandardDialogueUI)?.OnContinueConversation();
            }
            else
            {
                DialogueManager.displaySettings.subtitleSettings.continueButton
                    = DisplaySettings.SubtitleSettings.ContinueButtonMode.Always;
            }
        }

        private void OnBackLogTriggered()
        {
            backLogPanel.alpha = 1f;
            backLogPanel.interactable = true;
            backLogPanel.blocksRaycasts = true;

            // 자동진행 강제 종료
            if (isAuto)
                OnAutoPlayTriggered();
        }

        private void OnBackLogCloseTriggered()
        {
            backLogPanel.alpha = 0f;
            backLogPanel.interactable = false;
            backLogPanel.blocksRaycasts = false;
        }

        private void OnHideUITriggered()
        {
            // 숨기기
            if (!showUI.interactable)
            {
                dialoguePanel.alpha = 0f;
                dialoguePanel.interactable = false;
                dialoguePanel.blocksRaycasts = false;

                topMenuPanel.alpha = 0f;
                topMenuPanel.interactable = false;
                topMenuPanel.blocksRaycasts = false;

                showUI.interactable = true;
            }
            // 보이기
            else
            {
                dialoguePanel.alpha = 1f;
                dialoguePanel.interactable = true;
                dialoguePanel.blocksRaycasts = true;

                topMenuPanel.alpha = 1f;
                topMenuPanel.interactable = true;
                topMenuPanel.blocksRaycasts = true;

                showUI.interactable = false;
            }
        }


        private void OnSkipTriggered()
        {
            skipAlertPanel.alpha = 1f;
            skipAlertPanel.interactable = true;
            skipAlertPanel.blocksRaycasts = true;
        }

        private void OnCancelSkipTriggered()
        {
            skipAlertPanel.alpha = 0f;
            skipAlertPanel.interactable = false;
            skipAlertPanel.blocksRaycasts = false;
        }

        private void OnApproveSkipTriggered()
        {
            // 래퍼쪽에 호출주는게 낫겠지 (추후)
            DialogueManager.StopAllConversations();
        }
    }
}