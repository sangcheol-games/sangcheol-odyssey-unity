using SCOdyssey.Audio.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SCOdyssey.Audio.Hosting
{
    // 오디오 모듈의 프레임 구동. 다른 스크립트보다 먼저(-1010) FMOD update를 돌린다.
    // 도메인 리로드 뒤에는 모듈 참조가 사라지므로(엔진은 리로드 직전에 이미 종료됨) 스스로 멈춘다.
    [DefaultExecutionOrder(AudioExecutionOrder.EngineRunner)]
    [DisallowMultipleComponent]
    public sealed class AudioEngineRunner : MonoBehaviour
    {
        private AudioModule _module;

        internal void Bind(AudioModule module)
        {
            _module = module;
            enabled = true;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (_module == null || _module.IsShutDown)
            {
                enabled = false;
                return;
            }
            _module.Tick();
        }

        private void LateUpdate()
        {
            if (_module != null) _module.LateTick();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (_module != null) _module.OnFocusChanged(hasFocus);
        }

        private void OnApplicationQuit()
        {
            ShutdownModule();
        }

        private void OnDestroy()
        {
            ShutdownModule();
        }

        private void OnGUI()
        {
            if (_module == null || !AudioOverlay.Visible) return;
            AudioOverlay.Draw(_module);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RuntimeManagerGuard.Check("씬 로드: " + scene.name);
        }

        private void ShutdownModule()
        {
            if (_module == null) return;
            _module.Shutdown();
            _module = null;
        }
    }
}
