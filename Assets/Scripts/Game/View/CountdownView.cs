using System;
using TMPro;
using static SCOdyssey.Domain.Service.Constants;

namespace SCOdyssey.Game
{
    // 다음 마디 시작까지 남은 비트를 3/2/1로 보여주는 텍스트들. 레인이 아니라 슬롯(그룹 × 진행방향) 단위다.
    // 목표 시각에 닿으면 텍스트를 끈다
    public sealed class CountdownView
    {
        private readonly TextMeshProUGUI[] _texts;
        private readonly double?[] _targets = new double?[COUNTDOWN_SLOT_COUNT];
        private readonly int _maxCount;
        private readonly double _showWithinBeats;   // 경계에서 첫 숫자가 늦게 뜨지 않게 조금 여유를 둔다

        public CountdownView(TextMeshProUGUI[] texts, int countdownBeats, double epsilonBeats)
        {
            _texts = texts;
            _maxCount = countdownBeats;
            _showWithinBeats = countdownBeats + epsilonBeats;
        }

        public void Reset()
        {
            Array.Clear(_targets, 0, _targets.Length);
            for (int i = 0; i < COUNTDOWN_SLOT_COUNT; i++)
            {
                _texts[i].gameObject.SetActive(false);
                _texts[i].text = "";
            }
        }

        public void Activate(CountdownSlot slot, double targetTime)
        {
            _targets[(int)slot] = targetTime;
            _texts[(int)slot].gameObject.SetActive(true);
            _texts[(int)slot].text = "";
        }

        public void Tick(double time, double beatDuration)
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!_targets[i].HasValue) continue;

                double timeDiff = _targets[i].Value - time;
                if (timeDiff <= 0)
                {
                    _texts[i].gameObject.SetActive(false);
                    _targets[i] = null;
                    continue;
                }

                double remainingBeats = timeDiff / beatDuration;
                if (remainingBeats <= _showWithinBeats)
                {
                    int displayNum = (int)Math.Ceiling(remainingBeats);
                    if (displayNum > 0 && displayNum <= _maxCount)
                        _texts[i].text = displayNum.ToString();
                }
                else
                {
                    _texts[i].text = "";
                }
            }
        }
    }
}
