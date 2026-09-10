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
            // (Apply()는 IAudioManager 등록 후에 호출해야 볼륨 설정이 FMOD에 반영됨)
            var settingsManager = new SettingsManager();
            ServiceLocator.TryRegister<ISettingsManager>(settingsManager);
            settingsManager.Load();

            var inputManager = new InputManager();
            inputManager.Enable();
            ServiceLocator.TryRegister<IInputManager>(inputManager);

            // 등록만 먼저 하고 Init()(첫 UI 표시)은 모든 서비스 등록 후로 미룬다.
            // MainUI.OnEnable이 Instantiate 시점에 동기 실행되며 IAudioManager를 참조하기 때문.
            var uiManager = new UIManager();
            ServiceLocator.TryRegister<IUIManager>(uiManager);

            var musicManager = new MusicManager();
            ServiceLocator.TryRegister<IMusicManager>(musicManager);

            var characterManager = new CharacterManager();
            ServiceLocator.TryRegister<ICharacterManager>(characterManager);

            // FMODAudioManager는 MonoBehaviour이므로 AddComponent로 생성 (DontDestroyOnLoad 유지)
            var fmodAudio = gameObject.AddComponent<FMODAudioManager>();
            ServiceLocator.TryRegister<IAudioManager>(fmodAudio);

            // 모든 서비스가 준비된 뒤 설정 반영 (해상도/targetFrameRate/볼륨/입력 폴링)
            settingsManager.Apply();

            // 첫 UI(MainUI) 표시 — MainUI.OnEnable에서 BGM을 재생하므로 가장 마지막
            uiManager.Init();
        }


    }
}