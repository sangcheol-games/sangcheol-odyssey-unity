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
        private struct Entry
        {
            public string key;
            public PushMode mode;
            public Entry(string key, PushMode mode) { this.key = key; this.mode = mode; }
        }

        // 표시되는 UI Canvas의 기준 sortingOrder. 게임 씬의 오브젝트 레이어보다 높아야 함.
        private const int BaseSortingOrder = 100;

        // UI 기록과 인스턴스 소유를 분리한다.
        // _history : 기록만 보관 (UI 키 + 진입 모드). 뒤로가기 경로를 기억한다.
        // _instances : 실제 UI 인스턴스. 지연 생성 후 캐싱하여 재사용한다 (Destroy 하지 않음).
        private readonly Stack<Entry> _history = new Stack<Entry>();
        private readonly Dictionary<string, BaseUI> _instances = new Dictionary<string, BaseUI>();

        // 씬 바닥 경계: 이 Depth 하위의 UI는 현재 씬에서 사용하지 않는다.
        // MainScene에서는 0(로비 스택 전체 소유), 그 외 씬에서는 진입 시점의 스택 크기로 고정한다.
        private int _floor = 0;

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

            // MainScene: 로비 스택 전체를 소유 / 그 외 씬: 진입 시점 스택을 바닥 경계로 고정해 로비 UI를 숨긴다.
            _floor = scene.name == "MainScene" ? 0 : _history.Count;
            RefreshVisibility();
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

        // Canvas 1회 구성 (sortingOrder는 RefreshVisibility가 표시 깊이에 따라 매번 재할당)
        private void ConfigureCanvas(GameObject go)
        {
            Canvas canvas = go.GetComponent<Canvas>();

            // Screen Space - Camera: Camera.rect(레터박스)를 Canvas에도 적용
            canvas.renderMode      = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera     = Camera.main;
            canvas.planeDistance   = 10f;
            canvas.overrideSorting = true;

            // CanvasScaler 보장 (16:9 기준, Scale With Screen Size)
            var scaler = go.GetOrAddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;
        }

        private BaseUI GetUI(string key)
        {
            // 레지스트리에 캐시된 UI가 있으면 그대로 반환
            if (_instances.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // 캐시가 없으면 동적 생성 후 캐싱
            GameObject go = ResourceLoader.PrefabInstantiate($"UI/{key}");
            go.transform.SetParent(Root.transform);
            ConfigureCanvas(go);

            BaseUI ui = go.GetComponent<BaseUI>();
            _instances[key] = ui;
            return ui;
        }

        // UI 동적 표시
        public T ShowUI<T>(PushMode mode = PushMode.Push) where T : BaseUI
        {
            string key = typeof(T).Name;

            // 이미 최상단인 UI를 다시 요청하면 그대로 반환 (중복 push 방지)
            if (_history.Count > 0 && _history.Peek().key == key)
            {
                return (T)GetUI(key);
            }

            BaseUI ui = GetUI(key);

            // Replace: 현재 최상단 기록을 같은 Depth로 교체
            if (mode == PushMode.Replace && _history.Count > 0)
            {
                _history.Pop();
            }
            _history.Push(new Entry(key, mode));

            RefreshVisibility();
            return (T)ui;
        }

        // 입력 라우팅용: 현재 최상단 UI 반환 (없으면 null)
        // 최당산의 UI만 입력을 받도록 PeekUI()를 통해 라우팅한다. (UIManager는 입력 이벤트를 직접 처리하지 않음)
        public BaseUI PeekUI()
        {
            if (_history.Count == 0) return null;
            return _instances.TryGetValue(_history.Peek().key, out var ui) ? ui : null;
        }

        // UI 스택의 가장 위에 있는 UI 닫기 (캐시는 유지, Destroy 하지 않음)
        public void CloseUI(BaseUI closeUi)
        {
            if (_history.Count == 0)
            {
                return;
            }
            if (PeekUI() != closeUi)
            {
                Debug.Log("Close ui Failed!");
                return;
            }

            _history.Pop();
            RefreshVisibility();
        }

        // 표시 집합 계산 → 활성/비활성 토글 + sortingOrder 재할당을 위해 구분한다.
        // 표시 집합 구분 = 최상단부터 아래로, Overlay인 동안 그 아래까지 포함하고,
        //                 Overlay가 아닌 첫 UI를 포함한 뒤 정지. 단, 바닥경계(_floor) 밑으로는 내려가지 않음.
        private void RefreshVisibility()
        {
            Entry[] arr = _history.ToArray();           // arr[0] = 최상단
            int aboveFloor = arr.Length - _floor;       // 바닥경계 위(현재 씬 소유) 기록 수

            var visible = new List<BaseUI>();           // 0번이 최상단
            for (int i = 0; i < arr.Length && i < aboveFloor; i++)
            {
                visible.Add(GetUI(arr[i].key));
                if (arr[i].mode != PushMode.Overlay) break; // base 도달 ㅅㅣ 정지
            }

            // 표시 집합 외의 모든 캐시는 비활성화
            var visibleSet = new HashSet<BaseUI>(visible);
            foreach (var ui in _instances.Values)
            {
                if (ui == null) continue;
                if (!visibleSet.Contains(ui) && ui.gameObject.activeSelf)
                    ui.gameObject.SetActive(false);
            }

            // 표시 집합 활성화 + sortingOrder 재할당 (아래일수록 낮게, 최상단이 가장 높게)
            for (int i = 0; i < visible.Count; i++)
            {
                BaseUI ui = visible[i];
                if (ui.TryGetComponent<Canvas>(out var canvas))
                    canvas.sortingOrder = BaseSortingOrder + (visible.Count - 1 - i);
                if (!ui.gameObject.activeSelf)
                    ui.gameObject.SetActive(true);
            }
        }
    }
}
