using System;
using SCOdyssey.Game.Timing.LaneInput;
using UnityEngine;

namespace SCOdyssey.App
{
    public class InputManager : IInputManager
    {
        private InputSystem_Actions inputActions;

        // 레인 입력 소스(JudgementDriver가 매 프레임 비워 곡 시각으로 바꾼다). 레인 입력은 이 경로로만 나간다.
        private readonly UnityInputSystemTimestampSource _laneSource = new UnityInputSystemTimestampSource();
        public UnityInputSystemTimestampSource LaneTimestampSource => _laneSource;

        public event Action<Vector2> OnSelect;
        public event Action OnSubmit;
        public event Action OnCancel;
        public event Action OnRestart;
        public event Action OnPause;

        public bool IsInputActive { get; private set; } = true;

        public InputManager()
        {
            inputActions = new InputSystem_Actions();

            inputActions.Game.Lane1.performed += ctx => HandleLaneInput(1, ctx.time);
            inputActions.Game.Lane2.performed += ctx => HandleLaneInput(2, ctx.time);
            inputActions.Game.Lane3.performed += ctx => HandleLaneInput(3, ctx.time);
            inputActions.Game.Lane4.performed += ctx => HandleLaneInput(4, ctx.time);

            inputActions.Game.Lane1.canceled += ctx => HandleLaneRelease(1, ctx.time);
            inputActions.Game.Lane2.canceled += ctx => HandleLaneRelease(2, ctx.time);
            inputActions.Game.Lane3.canceled += ctx => HandleLaneRelease(3, ctx.time);
            inputActions.Game.Lane4.canceled += ctx => HandleLaneRelease(4, ctx.time);

            inputActions.Game.Restart.performed += _ => HandleRestart();
            inputActions.Game.Pause.performed += _ => HandlePause();

            inputActions.UI.Select.performed += ctx => HandleSelect(ctx.ReadValue<Vector2>());
            inputActions.UI.Submit.performed += _ => HandleSubmit();
            inputActions.UI.Cancel.performed += _ => HandleCancel();
        }

        private void HandleSelect(Vector2 dir) { if(IsInputActive) OnSelect?.Invoke(dir); }
        private void HandleSubmit() { if(IsInputActive) OnSubmit?.Invoke(); }
        private void HandleCancel() { if (IsInputActive) OnCancel?.Invoke(); }
        private void HandleLaneInput(int lane, double ctxTime)
        {
            if (!IsInputActive) return;
            _laneSource.Push(lane, true, ctxTime);
        }

        private void HandleLaneRelease(int lane, double ctxTime)
        {
            if (!IsInputActive) return;
            _laneSource.Push(lane, false, ctxTime);
        }
        private void HandleRestart() { if (IsInputActive) OnRestart?.Invoke(); }
        private void HandlePause()   { if (IsInputActive) OnPause?.Invoke(); }
        

        public void SwitchToUI()
        {
            DisableGameMap();
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
            DisableGameMap();
            inputActions.UI.Disable();
        }

        // 게임 맵을 끄면 눌려 있던 레인의 canceled가 그 자리에서 동기로 온다. 실제로 뗀 것이 아니므로 Synthetic으로 표시한다.
        private void DisableGameMap()
        {
            _laneSource.BeginSynthetic();
            try
            {
                inputActions.Game.Disable();
            }
            finally
            {
                _laneSource.EndSynthetic();
            }
        }

        public void SetInputActive(bool isActive) => IsInputActive = isActive;




    }
}
