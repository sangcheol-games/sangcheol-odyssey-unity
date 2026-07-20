using PixelCrushers.DialogueSystem;
using SCOdyssey.Core;
using SCOdyssey.Dialogue;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace SCOdyssey.App
{
    public class SCODialogueManager : MonoBehaviour, IDialogueManager
    {
        private IInputManager _inputManager;


        private void Awake()
        {
            ServiceLocator.TryRegister<IDialogueManager>(this);
            PixelCrushers.DialogueSystem.DialogueManager.SetDialogueSystemInput(false);
        }

        private void Start()
        {
            if (ServiceLocator.TryGet<IInputManager>(out _inputManager))
            {
                _inputManager.SwitchToDialogue();
                _inputManager.OnDialogueSelect += HandleLaneInput;
                _inputManager.OnDialogueSubmit += HandleLaneInput;
                _inputManager.OnDialogueCancel += HandleRestart;
            }
            else
            {
                Debug.LogError("[GameManager] IInputManager not found in ServiceLocator!");
            }

            scoreManager.OnScoreChanged += UpdateScore;
            scoreManager.OnComboChanged += UpdateCombo;
            scoreManager.OnGaugeChanged += UpdateGauge;

            DialogueManager.instance.conversationEnded += OnConversationEnded;
        }


        public bool LoadConversationData()
        {
            throw new NotImplementedException();
        }

        public void LoadDialogueScene(bool isFloating)
        {
            throw new NotImplementedException();
        }


        public void DialogueOnConversationEnd()
        {
            throw new NotImplementedException();
        }
    }

}