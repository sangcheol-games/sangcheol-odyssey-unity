using System;
using UnityEngine;

namespace SCOdyssey.App
{
    public class InputManager : IInputManager
    {
        private InputSystem_Actions inputActions;

        public event Action<Vector2> OnSelect;
        public event Action OnSubmit;
        public event Action OnCancel;
        public event Action<int> OnLanePressed;
        public event Action<int> OnLaneReleased;

        public bool IsInputActive { get; private set; } = true;


        public InputManager()
        {
            inputActions = new InputSystem_Actions();

            inputActions.Game.Lane1.performed += _ => HandleLaneInput(1);
            inputActions.Game.Lane2.performed += _ => HandleLaneInput(2);
            inputActions.Game.Lane3.performed += _ => HandleLaneInput(3);
            inputActions.Game.Lane4.performed += _ => HandleLaneInput(4);

            inputActions.Game.Lane1.canceled += _ => HandleLaneRelease(1);
            inputActions.Game.Lane2.canceled += _ => HandleLaneRelease(2);
            inputActions.Game.Lane3.canceled += _ => HandleLaneRelease(3);
            inputActions.Game.Lane4.canceled += _ => HandleLaneRelease(4);

            inputActions.UI.Select.performed += ctx => HandleSelect(ctx.ReadValue<Vector2>());
            inputActions.UI.Submit.performed += _ => HandleSubmit();
            inputActions.UI.Cancel.performed += _ => HandleCancel();
        }

        private void HandleSelect(Vector2 dir) { if(IsInputActive) OnSelect?.Invoke(dir); }
        private void HandleSubmit() { if(IsInputActive) OnSubmit?.Invoke(); }
        private void HandleCancel() { if (IsInputActive) OnCancel?.Invoke(); }
        private void HandleLaneInput(int lane) { if (IsInputActive) OnLanePressed?.Invoke(lane); }
        private void HandleLaneRelease(int lane) { if (IsInputActive) OnLaneReleased?.Invoke(lane); }
        

        public void SwitchToUI()
        {
            inputActions.Game.Disable();
            inputActions.UI.Enable();
        }

        public void SwitchToGameplay()
        {
            inputActions.UI.Disable();
            inputActions.Game.Enable();
        }


        public void Enable()
        {
            SwitchToUI(); // 기본적으로 UI 모드
        }

        public void Disable()
        {
            inputActions.Game.Disable();
            inputActions.UI.Disable();
        }

        public void SetInputActive(bool isActive) => IsInputActive = isActive;




    }
}
