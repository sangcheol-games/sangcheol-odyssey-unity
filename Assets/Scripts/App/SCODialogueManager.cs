using SCOdyssey.Core;
using SCOdyssey.Dialogue;
using PixelCrushers.DialogueSystem;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;


namespace SCOdyssey.App
{
    public class SCODialogueManager : IDialogueManager
    {
        private IInputManager _inputManager = null;
        private ISettingsManager _settingsManager = null;

        private DialogueAdditionalUI dialogueAdditionalUI = null;

        private DialogueDatabase currentDialogue = null;
        private bool isLoading = false;
        private StandardUIContinueButtonFastForward continueButton = null;
        private bool canSkip = true;        // TODO db에서 읽어와야 함

        private string languageCodeCurrent;



        // 오디오 (음성/sfx) 재생은 시퀀스 이용
        // 후일을 위한 메모...
        // db 엔트리 시퀀스에서 Audio / AudioWait(entrytag) 형식으로 호출
        // 오디오 클립 파일명을 entrytag 이름으로 맞춰 작성 (db Voiceover File 익스포트로 획득)
        // 로컬라이즈는 entrytag_locale, /locale/entrytag 파일/경로명으로 작성 시 자동인식 (아마)


        public event Action<bool> OnDialogueLoaded;

        public event Action OnConversationEnd;


        #region Lifecycle

        public SCODialogueManager()
        {
            if (ServiceLocator.TryGet<IInputManager>(out _inputManager))
            {
                _inputManager.OnDialogueSelect += HandleDialogueSelect;
                _inputManager.OnDialogueSubmit += HandleDialogueSubmit;
                _inputManager.OnDialogueCancel += HandleDialogueCancel;
            }
            else
            {
                Debug.LogError("[SCODialogueManager] IInputManager not found in ServiceLocator!");
            }


            if (ServiceLocator.TryGet<ISettingsManager>(out _settingsManager))
            {
                languageCodeCurrent = _settingsManager.Current.languageCode;

                _settingsManager.OnSettingsChanged += ((val) =>
                {
                    languageCodeCurrent = val.languageCode;
                });
            }
            else
            {
                Debug.LogError("[SCODialogueManager] ISettingsManager not found in ServiceLocator!");
            }
        }

        // 일반 객체로 수정

        //private void Awake()
        //{
        //    //ServiceLocator.TryRegister<IDialogueManager>(this);
        //}

        //private void Start()
        //{
        //    if (ServiceLocator.TryGet<IInputManager>(out _inputManager))
        //    {
        //        _inputManager.OnDialogueSelect += HandleDialogueSelect;
        //        _inputManager.OnDialogueSubmit += HandleDialogueSubmit;
        //        _inputManager.OnDialogueCancel += HandleDialogueCancel;
        //    }
        //    else
        //    {
        //        Debug.LogError("[SCODialogueManager] IInputManager not found in ServiceLocator!");
        //    }


        //    if (ServiceLocator.TryGet<ISettingsManager>(out _settingsManager))
        //    {
        //        languageCodeCurrent = _settingsManager.Current.languageCode;

        //        _settingsManager.OnSettingsChanged += ((val) =>
        //        {
        //            languageCodeCurrent = val.languageCode;
        //        });
        //    }
        //    else
        //    {
        //        Debug.LogError("[SCODialogueManager] ISettingsManager not found in ServiceLocator!");
        //    }
        //}

        //private void OnDestroy()
        //{
        //    ServiceLocator.Remove<IDialogueManager>();

        //    if (_inputManager != null)
        //    {
        //        _inputManager.OnDialogueSelect -= HandleDialogueSelect;
        //        _inputManager.OnDialogueSubmit -= HandleDialogueSubmit;
        //        _inputManager.OnDialogueCancel -= HandleDialogueCancel;
        //    }
        //}

        #endregion


        #region Interface

        public void LoadDialogueScene(bool isFloating)
        {
            if (isFloating)
                SceneManager.LoadSceneAsync("DialogueScene", LoadSceneMode.Additive);
            else
            {
                SceneManager.LoadScene("DialogueScene", LoadSceneMode.Single);
                // 기존 방식대로 Single
            }

            UnityAction<Scene, LoadSceneMode> action = null;
            action = (scene, mode) =>
            {
                DialogueManager.SetDialogueSystemInput(false);

                // UI 컴포넌트 획득
                var dialogueUI = DialogueManager.dialogueUI as StandardDialogueUI;
                if (dialogueUI == null)
                {
                    Debug.LogError($"[SCODialogueManager] LoadDialogue 다이얼로그UI 획득 실패");
                    return;
                }

                if (dialogueAdditionalUI == null)
                    dialogueAdditionalUI = dialogueUI.gameObject.GetComponent<DialogueAdditionalUI>();

                if (continueButton == null)
                    continueButton = dialogueUI.GetComponentInChildren<StandardUIContinueButtonFastForward>();

                SceneManager.sceneLoaded -= action;
            };

            SceneManager.sceneLoaded += action;


            _inputManager.SwitchToDialogue();

            DialogueManager.SetLanguage(languageCodeCurrent);
        }

        public void LoadDialogueScene(bool isFloating, string name, bool fromResource = false)
        {
            LoadDialogueScene(isFloating);

            UnityAction<Scene, LoadSceneMode> action = null;
            action = (scene, mode) => 
            {
                LoadDialogue(name, fromResource);

                SceneManager.sceneLoaded -= action;
            };

            SceneManager.sceneLoaded += action;
        }

        public void UnloadDialogueScene(bool isFloating)
        {
            if (continueButton != null)
                continueButton = null;
            if (dialogueAdditionalUI != null)
                dialogueAdditionalUI = null;


            if (currentDialogue != null)
                DialogueManager.RemoveDatabase(currentDialogue);


            dialogueAdditionalUI = null;
            _inputManager.SwitchToUI();

            if (isFloating && SceneManager.sceneCount > 1)
            {
                SceneManager.UnloadSceneAsync("DialogueScene");
            }
            else
            {
                SceneManager.LoadScene("MainScene");
            }
            // TODO: (메인 브랜치 병합 후) ui스택 호출 및 복원
        }


        public void LoadDialogue(string name, bool fromResource = false)
        {
            if (isLoading)  // 비동기처리용 간단 락
                return;


            if (currentDialogue != null)
                DialogueManager.RemoveDatabase(currentDialogue);

            currentDialogue = null;
            isLoading = true;

            // StreamingAssets (서버 연동 시 수정)
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

            // 스킵 불가 대화일 경우 스킵버튼 비활성화
            if (!canSkip)
                dialogueAdditionalUI.skipBtnObject.SetActive(false);
            else
                dialogueAdditionalUI.skipBtnObject.SetActive(true);

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


        #region Events

        private void HandleDialogueSelect(Vector2 input)
        {
            //
        }

        private void HandleDialogueSubmit()
        {
            if (DialogueManager.isConversationActive && continueButton != null)
                continueButton.OnFastForward();
        }

        private void HandleDialogueCancel()
        {
            if (!canSkip)   // 버튼입력 무시
                return;

            QuitConversation();
        }


        private void HandleConversationEnd(Transform actor)
        {
            OnConversationEnd?.Invoke();
        }

        #endregion

    }

}