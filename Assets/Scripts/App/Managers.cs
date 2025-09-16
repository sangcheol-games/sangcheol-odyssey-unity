using UnityEngine;

namespace SCOdyssey.App
{
    public class Managers : MonoBehaviour
    {
        private static Managers instance = null;
        public static Managers Instance { get { return instance; } }

        private static UIManager uiManager = new UIManager();
        public static UIManager UI { get {  Init(); return uiManager; } }


        private void Start()
        {
            Init();
        }

        private static void Init()
        {
            if (instance == null)
            {
                // @Managers 오브젝트 싱글톤으로 생성
                GameObject go = GameObject.Find("@Managers");
                if (go == null)
                {
                    go = new GameObject { name = "@Managers" };
                }

                // Managers 컴포넌트 Get 하여 instance에 적용
                if (go.TryGetComponent(out Managers managers))
                {
                    instance = managers;
                }
                else
                {
                    instance = go.AddComponent<Managers>();
                }

                DontDestroyOnLoad(go);

                uiManager.Init();

                Application.targetFrameRate = 60;   // 앱 프레임 60으로 고정
            }
        }
    }
}