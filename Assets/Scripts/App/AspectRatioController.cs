using System.Collections;
using UnityEngine;

namespace SCOdyssey.App
{
    [RequireComponent(typeof(Camera))]
    public class AspectRatioController : MonoBehaviour
    {
        private const float TargetAspect = 16f / 9f;
        private Camera _camera;
        private int _lastWidth;
        private int _lastHeight;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
        }

        private void Start()
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            StartCoroutine(RecalculateNextFrame());
        }

        private IEnumerator RecalculateNextFrame()
        {
            yield return null; // Screen.width/height는 다음 프레임에 확정됨
            RecalculateViewport();
        }

        private void Update()
        {
            // Screen.SetResolution은 비동기 → 실제로 해상도가 바뀐 프레임에 즉시 재계산
            if (Screen.width == _lastWidth && Screen.height == _lastHeight) return;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            RecalculateViewport();
        }

        private void RecalculateViewport()
        {
            float screenAspect = (float)Screen.width / Screen.height;

            if (Mathf.Approximately(screenAspect, TargetAspect))
            {
                _camera.rect = new Rect(0, 0, 1, 1);
            }
            else if (screenAspect < TargetAspect)
            {
                // 4:3처럼 세로가 긴 화면 → 위아래 검은 바
                float h = screenAspect / TargetAspect;
                _camera.rect = new Rect(0, (1 - h) * 0.5f, 1, h);
            }
            else
            {
                // 초와이드처럼 가로가 긴 화면 → 좌우 검은 바
                float w = TargetAspect / screenAspect;
                _camera.rect = new Rect((1 - w) * 0.5f, 0, w, 1);
            }
        }
    }
}
