using SCOdyssey.App.Interfaces;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.App
{
    public class Managers : MonoBehaviour
    {
        private static Managers instance = null;


        private void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            InitServices();
        }


        private void InitServices()
        {
            // 설정 매니저를 가장 먼저 등록하여 다른 매니저 초기화에 설정값 반영
            var settingsManager = new SettingsManager();
            ServiceLocator.TryRegister<ISettingsManager>(settingsManager);
            settingsManager.Load();
            settingsManager.Apply();

            var inputManager = new InputManager();
            inputManager.Enable();
            ServiceLocator.TryRegister<IInputManager>(inputManager);

            var uiManager = new UIManager();
            uiManager.Init();
            ServiceLocator.TryRegister<IUIManager>(uiManager);

            var musicManager = new MusicManager();
            ServiceLocator.TryRegister<IMusicManager>(musicManager);

            var characterManager = new CharacterManager();
            ServiceLocator.TryRegister<ICharacterManager>(characterManager);

            // FMODAudioManager는 MonoBehaviour이므로 AddComponent로 생성 (DontDestroyOnLoad 유지)
            var fmodAudio = gameObject.AddComponent<FMODAudioManager>();
            ServiceLocator.TryRegister<IAudioManager>(fmodAudio);

            // targetFrameRate는 SettingsManager.Apply()에서 설정값으로 적용됨
        }


    }
}