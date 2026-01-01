using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SCOdyssey.UI
{
    public class UIEventHandler : EventTrigger
    {
        public Action OnPressedHandler = null;

        private bool isPressed = false;

        private void Update()
        {
            //if (isPressed)  OnPressedHandler?.Invoke();
        }

        // TODO: InputAction을 통해 키보드 입력 핸들링 구현

        /*
        // 마우스 버튼or터치스크린이 내려가는 순간 호출
        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
            OnPointerDownHandler?.Invoke();
        }

        // 마우스 버튼or터치스크린이 올라가는 순간 호출
        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            OnPointerUpHandler?.Invoke();
        }
        */

    }
}
