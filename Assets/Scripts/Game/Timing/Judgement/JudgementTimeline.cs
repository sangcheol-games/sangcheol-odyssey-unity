using System;
using UnityEngine;

namespace SCOdyssey.Game.Timing.Judgement
{
    // 판정 싱크(judgmentOffset)를 적용하는 유일한 곳. 판정 입력 창을 단계 × 3ms만큼 옮긴다.
    // 단계 값은 세션마다 한 번만 읽는다(래치). 시점은 세션 Started, 놓쳤으면 첫 진행 프레임이다.
    internal sealed class JudgementTimeline
    {
        public const double StepSeconds = 0.003;

        private readonly Func<int> _readSteps;
        private bool _isLatched;
        private int _steps;

        public JudgementTimeline(Func<int> readSteps)
        {
            _readSteps = readSteps;
        }

        public bool IsLatched
        {
            get { return _isLatched; }
        }

        public int Steps
        {
            get { return _steps; }
        }

        // 이미 래치했으면 아무것도 하지 않는다.
        public void Latch()
        {
            if (_isLatched) return;
            _steps = ReadSteps();
            _isLatched = true;
        }

        // 새 세션을 붙일 때 부른다.
        public void Unlatch()
        {
            _isLatched = false;
            _steps = 0;
        }

        public double ToJudgeTime(double songTime)
        {
            return songTime - _steps * StepSeconds;
        }

        private int ReadSteps()
        {
            if (_readSteps == null) return 0;
            try
            {
                return _readSteps();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }
    }
}
