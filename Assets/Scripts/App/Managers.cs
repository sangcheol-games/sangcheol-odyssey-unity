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
            var uiManager = new UIManager();
            uiManager.Init();

            ServiceLocator.TryRegister<IUIManager>(uiManager);

            Application.targetFrameRate = 60;   // 앱 프레임 60으로 고정
        }


    }
}