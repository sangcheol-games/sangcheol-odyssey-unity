using System;
using UnityEngine;

namespace SCOdyssey.App
{
    public interface IInputManager
    {
        public event Action<Vector2> OnSelect;
        public event Action OnSubmit;
        public event Action OnCancel;
        public event Action<int, double> OnLanePressed; // 1~4번 레인 입력 이벤트 (laneIndex, inputDspTime)
        public event Action<int, double> OnLaneReleased; // 1~4번 레인 입력 해제 이벤트 (laneIndex, inputDspTime)
        public event Action OnRestart; // 게임 중 재시작 이벤트
        public event Action OnPause;   // 게임 중 일시정지 이벤트
        
        public bool IsInputActive { get; }
        public void SetInputActive(bool isActive);

        public void SetTimeSyncPoint(double dspTime, double realtimeNow);

        public void SwitchToUI();
        public void SwitchToGameplay();

        public void Enable();
        public void Disable();

    }
}
