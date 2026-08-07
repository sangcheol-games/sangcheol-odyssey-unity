using PixelCrushers.DialogueSystem;
using SCOdyssey.Core;
using SCOdyssey.Dialogue;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace SCOdyssey.App
{
    public class SCODialogueManager : MonoBehaviour, IDialogueManager
    {
        private IInputManager _inputManager;

        private DialogueDatabase currentDialogue = null;
        private bool isLoading = false;
        private StandardUIContinueButtonFastForward continueButton = null;


        public event Action<bool> OnDialogueLoaded;

        public event Action<Vector2> OnDialogueSelect;
        public event Action OnDialogueSubmit;
        public event Action OnDialogueCancel;

        public event Action OnConversationEnd;


        #region Interface

        public void LoadDialogueScene(bool isFloating)
        {
            if (isFloating)
                SceneManager.LoadSceneAsync("DialogueScene", LoadSceneMode.Additive);
            else
            {
                SceneManager.LoadScene("DialogueScene", LoadSceneMode.Single);
                // 카메라 옮겨오기? 필요한가
            }

            _inputManager.SwitchToDialogue();
        }

        public void LoadDialogueScene(bool isFloating, string name, bool fromResource = false)
        {
            LoadDialogueScene(isFloating);

            LoadDialogue(name, fromResource);
        }

        public void UnloadDialogueScene(bool isFloating)
        {
            if (currentDialogue != null)
                DialogueManager.RemoveDatabase(currentDialogue);


            if (isFloating && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync("DialogueScene");
            else
            {
                _inputManager.SwitchToUI();
                SceneManager.LoadScene("MainScene");
                // TODO: (메인 브랜치 병합 후) ui스택 호출 및 복원
            }
        }


        public void LoadDialogue(string name, bool fromResource = false)
        {
            if (isLoading)  // 비동기처리용 간단 락
                return;


            if (currentDialogue != null)
                DialogueManager.RemoveDatabase(currentDialogue);

            currentDialogue = null;
            isLoading = true;

            // StreamingAssets (추후수정)
            if (!fromResource)
            {
                string bundlePath = System.IO.Path.Combine(Application.streamingAssetsPath, "Dialogue", name);
                AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);


                request.completed += (operation) =>
                {
                    AssetBundle bundle = request.assetBundle;

                    if (bundle == null)
                    {
                        Debug.LogError($"[SCODialogueManager] LoadDialogue 실패 | 경로: {bundlePath}");
                        isLoading = false;
                        OnDialogueLoaded?.Invoke(false);
                        return;
                    }

                    AssetBundleRequest assetRequest = bundle.LoadAssetAsync<DialogueDatabase>(name);
                    assetRequest.completed += (assetOp) =>
                    {
                        if (currentDialogue == null)
                        {
                            Debug.LogError($"[SCODialogueManager] LoadDialogue 에셋 획득 실패 | 파일: {name}");
                            isLoading = false;
                            OnDialogueLoaded?.Invoke(false);
                            return;
                        }

                        DialogueManager.AddDatabase(currentDialogue);
                        bundle.Unload(false);
                        isLoading = false;
                        OnDialogueLoaded?.Invoke(true);
                    };
                };
            }
            // 개발용 Resource에서 로드
            else
            {
                string filePath = System.IO.Path.Combine("Dialogue", name);

                ResourceRequest request = Resources.LoadAsync<DialogueDatabase>(filePath);

                request.completed += (operation) =>
                {
                    currentDialogue = request.asset as DialogueDatabase;

                    if (currentDialogue == null)
                    {
                        Debug.LogError($"[SCODialogueManager] LoadDialogue 에셋 획득 실패 | 파일: {name}");
                        isLoading = false;
                        OnDialogueLoaded?.Invoke(false);
                        return;
                    }

                    DialogueManager.AddDatabase(currentDialogue);
                    isLoading = false;
                    OnDialogueLoaded?.Invoke(true);
                };
            }
            
        }


        public bool PlayConversation(string conversation)
        {
            if (conversation == null ||
                DialogueManager.MasterDatabase.GetConversation(conversation) == null)
            {
                Debug.LogError($"[SCODialogueManager] PlayConversation 실행 실패 | 타이틀: {conversation}");
                return false;
            }

            DialogueManager.StartConversation(conversation);
            DialogueManager.instance.conversationEnded += HandleConversationEnd;

            return true;
        }

        public void QuitConversation()
        {
            DialogueManager.StopAllConversations();
            DialogueManager.instance.conversationEnded -= HandleConversationEnd;
        }

        #endregion


        #region Lifecycle

        private void Awake()
        {
            ServiceLocator.TryRegister<IDialogueManager>(this);

            PixelCrushers.DialogueSystem.DialogueManager.SetDialogueSystemInput(false);
        }

        private void Start()
        {
            if (ServiceLocator.TryGet<IInputManager>(out _inputManager))
            {
                //_inputManager.SwitchToDialogue();
                _inputManager.OnDialogueSelect += HandleDialogueSelect;
                _inputManager.OnDialogueSubmit += HandleDialogueSubmit;
                _inputManager.OnDialogueCancel += HandleDialogueCancel;
            }
            else
            {
                Debug.LogError("[SCODialogueManager] IInputManager not found in ServiceLocator!");
            }

            //DialogueManager.instance.conversationEnded += (var) => OnConversationEnd?.Invoke();
        }

        private void OnDestroy()
        {
            ServiceLocator.Remove<IDialogueManager>();

            if (_inputManager != null)
            {
                _inputManager.OnDialogueSelect -= HandleDialogueSelect;
                _inputManager.OnDialogueSubmit -= HandleDialogueSubmit;
                _inputManager.OnDialogueCancel -= HandleDialogueCancel;
                //_inputManager.SwitchToUI();
            }
        }

        #endregion


        #region Events

        private void HandleDialogueSelect(Vector2 input)
        {
            // ? 어케쓰지
        }

        private void HandleDialogueSubmit()
        {
            if (DialogueManager.isConversationActive && continueButton != null)
            {
                continueButton.OnFastForward();
            }
        }

        private void HandleDialogueCancel()
        {
            QuitConversation();
        }


        private void HandleConversationEnd(Transform actor)
        {
            OnConversationEnd?.Invoke();
        }

        #endregion

    }

}