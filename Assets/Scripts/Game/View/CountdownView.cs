using System;
using UnityEngine;
using UnityEngine.UI;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 다음 마디 시작까지 남은 비트를 3/2/1 스프라이트로 보여주는 이미지들. 레인이 아니라 슬롯(그룹 × 진행방향) 단위다.
    // 목표 시각에 닿으면 이미지를 끈다. 스프라이트는 숫자가 바뀔 때만 대입한다(ContentSizeFitter의 불필요한 리빌드 방지)
    public sealed class CountdownView
    {
        private readonly Image[] _images;
        private readonly Sprite[] _sprites;   // 인덱스 = 표시 숫자 - 1
        private readonly double?[] _targets = new double?[COUNTDOWN_SLOT_COUNT];
        private readonly int[] _shown = new int[COUNTDOWN_SLOT_COUNT];   // 슬롯이 지금 보여주는 숫자. 0 = 없음
        private readonly double _maxCount;
        private readonly double _showWithinBeats;   // 경계에서 첫 숫자가 늦게 뜨지 않게 조금 여유를 둔다

        public CountdownView(Image[] images, Sprite[] sprites, double countdownBeats, double epsilonBeats)
        {
            _images = images;
            _sprites = sprites;
            _maxCount = countdownBeats;
            _showWithinBeats = countdownBeats + epsilonBeats;

            if (countdownBeats > sprites.Length)
                Debug.LogWarning($"[CountdownView] 카운트다운 {countdownBeats}비트에 스프라이트가 {sprites.Length}장뿐이다. 큰 숫자는 비워 둔다");
        }

        public void Reset()
        {
            Array.Clear(_targets, 0, _targets.Length);
            Array.Clear(_shown, 0, _shown.Length);
            for (int i = 0; i < COUNTDOWN_SLOT_COUNT; i++)
            {
                _images[i].enabled = false;
                _images[i].gameObject.SetActive(false);
            }
        }

        // 이미 같은 목표 시각으로 켜져 있으면 그대로 둔다(한 프레임 깜빡임 방지)
        public void Activate(CountdownSlot slot, double targetTime)
        {
            int i = (int)slot;
            if (_targets[i].HasValue && Math.Abs(_targets[i].Value - targetTime) < 0.01) return;

            _targets[i] = targetTime;
            _shown[i] = 0;
            _images[i].enabled = false;   // 숫자 범위에 들어올 때까지는 빈 상태
            _images[i].gameObject.SetActive(true);
        }

        public void Tick(double time, double beatDuration)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].HasValue) continue;

                double timeDiff = _targets[i].Value - time;
                if (timeDiff <= 0)
                {
                    _shown[i] = 0;
                    _images[i].gameObject.SetActive(false);
                    _targets[i] = null;
                    continue;
                }

                double remainingBeats = timeDiff / beatDuration;
                if (remainingBeats <= _showWithinBeats)
                {
                    int displayNum = (int)Math.Ceiling(remainingBeats);
                    if (displayNum > 0 && displayNum <= _maxCount)
                        Show(i, displayNum);
                }
                else
                {
                    Show(i, 0);
                }
            }
        }

        // num = 0이면 이미지를 숨긴다. 값이 바뀔 때만 sprite를 대입한다
        private void Show(int slot, int num)
        {
            if (_shown[slot] == num) return;
            _shown[slot] = num;

            Image image = _images[slot];
            if (num <= 0 || num > _sprites.Length)
            {
                image.enabled = false;
                return;
            }

            image.sprite = _sprites[num - 1];
            image.enabled = true;
        }
    }
}
