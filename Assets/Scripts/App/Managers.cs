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

            // 콘텐츠 제공 방식 선택:
            // - 현재: LocalContentProvider (StreamingAssets 직접 읽기, 테스트/개발용)
            // - CDN 전환 시: USE_CDN_DELIVERY 심볼 추가 → AddressablesContentProvider 활성화
            // 마이그레이션 절차: Assets/Scripts/Game/CONTENT_DELIVERY_MIGRATION.md
#if USE_CDN_DELIVERY
            IContentProvider contentProvider = new AddressablesContentProvider();
#else
            IContentProvider contentProvider = new LocalContentProvider();
#endif
            contentProvider.InitializeAsync(); // LocalContentProvider: no-op (즉시 완료)
            ServiceLocator.TryRegister<IContentProvider>(contentProvider);

            Application.targetFrameRate = 60;   // 앱 프레임 60으로 고정
        }


    }
}