using System;
using UnityEngine;

namespace SCOdyssey.App
{
    public interface IInputManager
    {
        public event Action<Vector2> OnSelect;
        public event Action OnSubmit;
        public event Action OnCancel;
        public event Action<int> OnLanePressed; // 1~4번 레인 입력 이벤트
        public event Action<int> OnLaneReleased; // 1~4번 레인 입력 해제 이벤트
        
        public bool IsInputActive { get; }
        public void SetInputActive(bool isActive);

        public void SwitchToUI();
        public void SwitchToGameplay();

        public void Enable();
        public void Disable();

    }
}
