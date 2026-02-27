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
            var inputManager = new InputManager();
            inputManager.Enable();
            ServiceLocator.TryRegister<IInputManager>(inputManager);

            var uiManager = new UIManager();
            uiManager.Init();
            ServiceLocator.TryRegister<IUIManager>(uiManager);

            var musicManager = new MusicManager();
            ServiceLocator.TryRegister<IMusicManager>(musicManager);

            // FMODAudioManager는 MonoBehaviour이므로 AddComponent로 생성 (DontDestroyOnLoad 유지)
            var fmodAudio = gameObject.AddComponent<FMODAudioManager>();
            ServiceLocator.TryRegister<IAudioManager>(fmodAudio);

            Application.targetFrameRate = 60;   // 앱 프레임 60으로 고정
        }


    }
}