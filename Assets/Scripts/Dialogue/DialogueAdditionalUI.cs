using PixelCrushers.DialogueSystem;
using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;
using UnityEngine.UI;


namespace SCOdyssey.Dialogue
{
    public class DialogueAdditionalUI : MonoBehaviour
    {
        private IDialogueManager _SCODialogueManager;


        private bool isAuto;

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



        private void OnEnable()
        {
            if (!ServiceLocator.TryGet<IDialogueManager>(out _SCODialogueManager))
                Debug.LogError("[DialogueAdditionalUI] IDialogueManager not found in ServiceLocator!");


            // 기본값 세팅
            DialogueManager.displaySettings.subtitleSettings.subtitleCharsPerSecond = 40;
            DialogueManager.displaySettings.subtitleSettings.minSubtitleSeconds = 3;


            isAuto = false;

            ToggleUI(backLogPanel, false);

            ToggleUI(skipAlertPanel, false);


            autoPlay.onClick.AddListener(OnAutoPlayTriggered);
            backLog.onClick.AddListener(OnBackLogTriggered);
            hideUI.onClick.AddListener(OnHideUITriggered);
            skip.onClick.AddListener(OnSkipTriggered);

            backLogClose.onClick.AddListener(OnBackLogCloseTriggered);
            backLogCloseBg.onClick.AddListener(OnBackLogCloseTriggered);    // 투명버튼

            showUI.onClick.AddListener(OnHideUITriggered);                  // 투명버튼

            cancelSkip.onClick.AddListener(OnCancelSkipTriggered);
            cancelSkipBg.onClick.AddListener(OnCancelSkipTriggered);        // 투명버튼
            approveSkip.onClick.AddListener(OnApproveSkipTriggered);


            // 매니저 찾아다가 호출..
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
            ToggleUI(backLogPanel, true);

            // 자동진행 강제 종료
            if (isAuto)
                OnAutoPlayTriggered();
        }

        private void OnBackLogCloseTriggered()
        {
            ToggleUI(backLogPanel, false);
        }

        private void OnHideUITriggered()
        {
            // 숨기기
            if (!showUI.interactable)
            {
                ToggleUI(dialoguePanel, false);

                ToggleUI(topMenuPanel, false);

                showUI.interactable = true;
            }
            // 보이기
            else
            {
                ToggleUI(dialoguePanel, true);

                ToggleUI(topMenuPanel, true);

                showUI.interactable = false;
            }
        }


        private void OnSkipTriggered()
        {
            ToggleUI(skipAlertPanel, true);
        }

        private void OnCancelSkipTriggered()
        {
            ToggleUI(skipAlertPanel, false);
        }

        private void OnApproveSkipTriggered()
        {
            _SCODialogueManager.QuitConversation();
        }


        // 헬퍼
        private void ToggleUI(CanvasGroup canvas, bool toggle)
        {
            if (toggle)
            {
                canvas.alpha = 1f;
                canvas.interactable = true;
                canvas.blocksRaycasts = true;
            }
            else
            {
                canvas.alpha = 0f;
                canvas.interactable = false;
                canvas.blocksRaycasts = false;
            }
        }
    }
}