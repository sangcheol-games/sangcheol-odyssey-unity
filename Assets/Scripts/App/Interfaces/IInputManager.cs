using System;
using UnityEngine;

namespace SCOdyssey.App
{
    public interface IInputManager
    {
        public event Action<Vector2> OnSelect;
        public event Action OnSubmit;
        public event Action OnCancel;
        // 레인 입력(1~4)은 이벤트가 아니라 InputManager.LaneTimestampSource → JudgementDriver 경로로 전달된다.
        public event Action OnRestart; // 게임 중 재시작 이벤트
        public event Action OnPause;   // 게임 중 일시정지 이벤트
        
        public bool IsInputActive { get; }
        public Vector2 SelectValue { get; } // 지금 눌려 있는 UI 방향 값(홀드 반복용). 입력 비활성·UI 맵 꺼짐이면 0
        public void SetInputActive(bool isActive);

        public void SwitchToUI();
        public void SwitchToGameplay();

        public void Enable();
        public void Disable();

    }
}
