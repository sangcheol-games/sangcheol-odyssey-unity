using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.Game
{
    // 노트 히트 프레임 재생. Image.sprite를 fps 간격으로 교체하는 것 외에는 아무것도 하지 않는다.
    // AnimationClip은 단독 재생이 불가능하고(Animator 필요), 레거시 Animation은 스프라이트 스왑(오브젝트 참조 커브)을
    // 지원하지 않으므로 프레임을 코드로 직접 넘긴다. NotePrefab의 NoteImage 자식에 부착하고,
    // NoteController가 GetComponentInChildren으로 자동 결선한다.
    public class NoteHitAnimation : MonoBehaviour
    {
        [SerializeField] private Image targetImage;   // 비워두면 자기 자신에서 찾음
        [SerializeField] private Sprite[] hitFrames;
        [SerializeField] private float fps = 24f;

        private Coroutine _playing;

        public bool HasFrames => hitFrames != null && hitFrames.Length > 0;

        private void Awake()
        {
            if (targetImage == null)
                targetImage = GetComponent<Image>();
        }

        // 히트 프레임을 순서대로 재생하고 마지막 프레임을 넘긴 뒤 onFinished를 호출한다.
        // 프레임이나 대상 Image가 없으면(아트 미적용 상태) 즉시 onFinished를 불러 판정 흐름이 끊기지 않게 한다.
        public void Play(Action onFinished)
        {
            if (!HasFrames || targetImage == null)
            {
                onFinished?.Invoke();
                return;
            }

            StopAndReset();
            _playing = StartCoroutine(PlayRoutine(onFinished));
        }

        // 진행 중인 재생을 중단한다. 스프라이트 복원은 NoteController.Init이 담당(풀 재사용 시)
        public void StopAndReset()
        {
            if (_playing != null)
            {
                StopCoroutine(_playing);
                _playing = null;
            }
        }

        // 스킨 주입 이음매. NoteSkinSO를 들고 있는 매니저가 런타임에 프레임을 갈아끼울 수 있다.
        public void SetFrames(Sprite[] frames, float framesPerSecond)
        {
            hitFrames = frames;
            if (framesPerSecond > 0f)
                fps = framesPerSecond;
        }

        private IEnumerator PlayRoutine(Action onFinished)
        {
            float interval = fps > 0f ? 1f / fps : 0f;

            for (int i = 0; i < hitFrames.Length; i++)
            {
                if (hitFrames[i] != null)
                    targetImage.sprite = hitFrames[i];

                if (interval > 0f)
                    yield return new WaitForSeconds(interval);
                else
                    yield return null;
            }

            _playing = null;
            onFinished?.Invoke();
        }
    }
}
