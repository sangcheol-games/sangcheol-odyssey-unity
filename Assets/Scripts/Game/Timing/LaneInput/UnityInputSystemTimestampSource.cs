using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.Game.Timing.LaneInput
{
    // Input System 레인 콜백의 입력을 모아 두는 소스(1단계). 생산자(Input System 콜백)와 소비자(JudgementDriver)가 모두 메인 스레드다.
    //   - Push(lane, isDown, ctx.time): ctx.time을 QPC로 바꿔 256칸 링에 넣는다. 넘치면 오래된 것부터 버리고 센다.
    //   - Synthetic 표시: BeginSynthetic/EndSynthetic 구간(입력 맵 비활성화로 생기는 cancel)과 포커스가 없을 때 들어온 입력.
    //   - Drain은 매 프레임 불리며, 그때 시각 변환 표본도 하나 넣는다.
    public sealed class UnityInputSystemTimestampSource : IInputTimestampSource
    {
        public const int Capacity = 256;

        private readonly RealtimeQpcMapper _mapper;
        private readonly Func<bool> _isFocused;
        private readonly LaneInputEvent[] _ring = new LaneInputEvent[Capacity];
        private int _head;
        private int _count;
        private int _syntheticDepth;

        public UnityInputSystemTimestampSource()
            : this(new RealtimeQpcMapper(), ReadApplicationFocus)
        {
        }

        internal UnityInputSystemTimestampSource(RealtimeQpcMapper mapper, Func<bool> isFocused)
        {
            _mapper = mapper;
            _isFocused = isFocused;
        }

        public int PushedCount { get; private set; }
        public int SyntheticCount { get; private set; }
        public int DroppedCount { get; private set; }

        public double MapperOffsetSeconds
        {
            get { return _mapper.OffsetSeconds; }
        }

        public bool IsInSyntheticScope
        {
            get { return _syntheticDepth > 0; }
        }

        internal RealtimeQpcMapper Mapper
        {
            get { return _mapper; }
        }

        // InputManager의 레인 콜백에서 부른다(IsInputActive일 때만).
        public void Push(int lane, bool isDown, double inputTime)
        {
            if (!_mapper.HasOffset) _mapper.Sample();

            bool synthetic = _syntheticDepth > 0 || !_isFocused();
            var e = new LaneInputEvent(lane, isDown, _mapper.ToQpc(inputTime), synthetic);

            if (_count == Capacity)
            {
                _head = (_head + 1) % Capacity;
                _count--;
                DroppedCount++;
            }
            _ring[(_head + _count) % Capacity] = e;
            _count++;
            PushedCount++;
            if (synthetic) SyntheticCount++;
        }

        // 입력 맵을 끄는 코드(SwitchToUI, Disable)를 감싼다. 그 안에서 동기로 오는 cancel은 Synthetic이 된다.
        public void BeginSynthetic()
        {
            _syntheticDepth++;
        }

        public void EndSynthetic()
        {
            if (_syntheticDepth > 0) _syntheticDepth--;
        }

        public int Drain(List<LaneInputEvent> into)
        {
            _mapper.Sample();
            int drained = _count;
            for (int i = 0; i < drained; i++)
            {
                into.Add(_ring[(_head + i) % Capacity]);
            }
            _head = 0;
            _count = 0;
            return drained;
        }

        private static bool ReadApplicationFocus()
        {
            return Application.isFocused;
        }
    }
}
