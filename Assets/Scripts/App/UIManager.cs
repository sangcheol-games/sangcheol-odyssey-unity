using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SCOdyssey.UI;
using SCOdyssey.Core;
using Unity.VisualScripting;

namespace SCOdyssey.App
{
    public class UIManager : IUIManager
    {
        private int order = -20;    // sorting order에 사용할 변수
        private Stack<BaseUI> uiStack = new Stack<BaseUI>();
        public GameObject Root
        {
            get
            {
                GameObject root = GameObject.Find("@UI_Root");
                if (root == null)
                {
                    root = new GameObject { name = "@UI_Root" };
                    Object.DontDestroyOnLoad(root);
                }

                return root;
            }
        }
        public void Init()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            ShowUI<MainUI>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshCamera();
            // MainScene: 스택에 보존된 UI를 다시 표시 / 그 외 씬: UI 숨김 (스택은 유지)
            bool isMainScene = scene.name == "MainScene";
            foreach (Transform child in Root.transform)
                child.gameObject.SetActive(isMainScene);
        }

        // 씬 전환 후 @UI_Root의 Canvas worldCamera를 새 씬의 Camera.main으로 갱신
        // (@UI_Root는 DontDestroyOnLoad이므로 씬 전환 시 worldCamera가 null이 됨)
        // @UI_Root 아래에는 UIManager가 생성한 UI만 존재하므로 조건 없이 모든 Canvas를 갱신
        private void RefreshCamera()
        {
            if (Camera.main == null) return;
            foreach (Transform child in Root.transform)
            {
                if (child.TryGetComponent<Canvas>(out var canvas))
                {
                    canvas.renderMode  = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = Camera.main;
                }
            }
        }

        private void SetCanvas(GameObject go, bool sort = true)
        {
            Canvas canvas = go.GetComponent<Canvas>();

            // Screen Space - Camera: Camera.rect(레터박스)를 Canvas에도 적용
            canvas.renderMode    = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera   = Camera.main;
            canvas.planeDistance = 10f;
            canvas.overrideSorting = true;

            if (sort) { canvas.sortingOrder = order++; }
            else      { canvas.sortingOrder = 0; }

            // CanvasScaler 보장 (16:9 기준, Scale With Screen Size)
            var scaler = go.GetOrAddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;
        }

        // UI 동적 생성
        public T ShowUI<T>(string name = null, Transform parent = null) where T : BaseUI
        {
            T peekUI = PeekUI<T>();
            if (peekUI != null)
            {
                return peekUI; // 이미 활성화된 UI가 있으면 그것을 반환
            }

            // 이름이 없다면 타입을 이름으로 사용
            if (string.IsNullOrEmpty(name))
            {
                name = typeof(T).Name;
            }
            // UI 생성 후 스택에 넣기
            GameObject go = ResourceLoader.PrefabInstantiate($"UI/{name}");
            T ui = go.GetComponent<T>();
            uiStack.Push(ui);
            SetCanvas(go);

            // 부모 설정
            if (parent != null)
            {
                go.transform.SetParent(parent);
            }
            else
            {
                // parameter가 없다면 @UI_Root를 부모로 설정
                go.transform.SetParent(Root.transform);
            }
            return ui;
        }

        // UI 스택에서 T타입의 UI를 return
        private T FindUI<T>() where T : BaseUI
        {
            foreach (var item in uiStack)
            {
                if (item is T)
                {
                    return item as T;
                }
            }
            return null;
        }

        // UI스택의 가장 위에 있는 UI return
        private T PeekUI<T>() where T : BaseUI
        {
            // 스택이 비어있으면 null return
            if (uiStack.Count == 0)
            {
                return null;
            }
            // 스택의 가장 위 UI와 T의 타입이 맞지 않으면 null return
            return uiStack.Peek() as T;
        }
        
        // UI 스택의 가장 위에 있는 UI 닫기
        public void CloseUI(BaseUI closeUi)
        {
            // 스택이 비어있으면 return
            if (uiStack.Count == 0)
            {
                return;
            }
            // 가장 위에 있는 UI가 닫으려는 UI와 다르다면 Fail로그 출력 후 return
            if (uiStack.Peek() != closeUi)
            {
                Debug.Log("Close ui Failed!");
                return;
            }

            // UI 스택에서 Pop & Destroy
            BaseUI destroyUi = uiStack.Pop();
            Object.Destroy(destroyUi.gameObject);

            Canvas canvas = closeUi.GetComponent<Canvas>();

            // SetCanvas가 된 UI를 닫을 때는 order 되돌리기
            if (canvas.sortingOrder < 0)
            {
                order--;
            }
        }
    }
}
