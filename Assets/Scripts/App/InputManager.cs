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
        public event Action<int, double> OnLanePressed;
        public event Action<int, double> OnLaneReleased;
        public event Action OnRestart;
        public event Action OnPause;

        public bool IsInputActive { get; private set; } = true;

        private double _dspAtSync;
        private double _realtimeAtSync;
        private bool _hasSyncPoint = false;

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
        private void HandleLaneInput(int lane, double ctxTime) { if (IsInputActive) OnLanePressed?.Invoke(lane, ConvertToDspTime(ctxTime)); }
        private void HandleLaneRelease(int lane, double ctxTime) { if (IsInputActive) OnLaneReleased?.Invoke(lane, ConvertToDspTime(ctxTime)); }
        private void HandleRestart() { if (IsInputActive) OnRestart?.Invoke(); }
        private void HandlePause()   { if (IsInputActive) OnPause?.Invoke(); }
        

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

        public void SetTimeSyncPoint(double dspTime, double realtimeNow)
        {
            _dspAtSync = dspTime;
            _realtimeAtSync = realtimeNow;
            _hasSyncPoint = true;
        }

        private double ConvertToDspTime(double ctxTime)
        {
            if (!_hasSyncPoint) return UnityEngine.AudioSettings.dspTime; // 폴백
            return _dspAtSync + (ctxTime - _realtimeAtSync);
        }




    }
}
