using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SCOdyssey.UI
{
    public class UIEventHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Action OnPressedHandler = null;
        public Action OnClickedHandler = null;
        public Action OnPointerDownHandler = null;
        public Action OnPointerUpHandler = null;

        private bool isPressed = false;

        private void Update()
        {
            if (isPressed)
            {
                OnPressedHandler?.Invoke();
            }
        }

        // 클릭이 완료되는 순간 호출
        public void OnPointerClick(PointerEventData eventData)
        {
            OnClickedHandler?.Invoke();
        }

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

    }
}
