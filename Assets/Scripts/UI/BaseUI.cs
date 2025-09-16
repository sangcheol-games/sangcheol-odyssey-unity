using System;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

namespace SCOdyssey.UI
{
    public class BaseUI : MonoBehaviour
    {
        public enum UIEvent
        {
            Clicked,
            Pressed,    
            PointerDown,
            PointerUp
        }

        // UI 캐싱 딕셔너리
        private Dictionary<Type, UnityEngine.Object[]> objDic = new Dictionary<Type, UnityEngine.Object[]>();

        // 바인딩 할 오브젝트의 이름을 Enum타입으로 받아와 딕셔너리에 저장
        protected void Bind<T>(Type type) where T : UnityEngine.Object
        {
            string[] names = Enum.GetNames(type);   // reflection 작업. UI호출 중 1번만 사용되므로 성능 이슈 없음
            UnityEngine.Object[] objects = new UnityEngine.Object[names.Length];
            objDic.Add(typeof(T), objects);

            for (int i = 0; i < names.Length; i++)
            {
                if (typeof(T) == typeof(GameObject))
                {
                    objects[i] = FindChild(gameObject, names[i], true);
                }
                else
                {
                    objects[i] = FindChild<T>(gameObject, names[i], true);
                }

                if (objects[i] == null)
                {
                    Debug.Log($"Failed to bind({names[i]})");
                }
            }
        }
        // UIBase를 상속 받는 UI 스크립트에서 사용 할 Bind 함수들
        protected void BindObject(Type type)
        {
            Bind<GameObject>(type);
        }
        protected void BindImage(Type type)
        {
            Bind<Image>(type);
        }
        protected void BindText(Type type)
        {
            Bind<TMP_Text>(type);
        }
        protected void BindButton(Type type)
        {
            Bind<Button>(type);
        }

        protected void BindEvent(GameObject go, Action action, UIEvent type)
        {
            UIEventHandler evt = go.GetOrAddComponent<UIEventHandler>();

            switch (type)
            {
                case UIEvent.Clicked:
                    evt.OnClickedHandler -= action;
                    evt.OnClickedHandler += action;
                    break;
                case UIEvent.Pressed:
                    evt.OnPressedHandler -= action;
                    evt.OnPressedHandler += action;
                    break;
                case UIEvent.PointerDown:
                    evt.OnPointerDownHandler -= action;
                    evt.OnPointerDownHandler += action;
                    break;
                case UIEvent.PointerUp:
                    evt.OnPointerUpHandler -= action;
                    evt.OnPointerUpHandler += action;
                    break;
            }
        }

        // 캐싱 딕셔너리에서 T타입의 인덱스에 해당하는 값을 반환
        // Enum타입으로 저장하였으므로 Enum타입의 값을 int로 변환하여 사용
        protected T Get<T>(int idx) where T : UnityEngine.Object
        {
            UnityEngine.Object[] objects = null;
            if (objDic.TryGetValue(typeof(T), out objects) == false)
            {
                return null;
            }
            return objects[idx] as T;
        }


        // UIBase를 상속 받는 UI 스크립트에서 사용 할 Get 함수들
        protected GameObject GetObject(int idx)
        {
            return Get<GameObject>(idx);
        }
        protected TMP_Text GetText(int idx)
        {
            return Get<TMP_Text>(idx);
        }
        protected Button GetButton(int idx)
        {
            return Get<Button>(idx);
        }
        protected Image GetImage(int idx)
        {
            return Get<Image>(idx);
        }


        // GameObject의 컴포넌트 반환하기 위해 사용
        private T FindChild<T>(GameObject go, string name = null, bool recursive = false) where T : UnityEngine.Object
        {
            if (go == null)
            {
                return null;
            }
            // 재귀적으로 검색할지 여부 결정
            // recursive == false인 경우 -> 직계 자식만 검사
            // recursive == true인 경우 -> 모든 하위 오브젝트 검사
            if (recursive == false)
            {
                // GameObject.Find -> 씬 모두 검사, transform.Find -> 자식들만 검사
                Transform transform = go.transform.Find(name);
                if (transform != null)
                {
                    return transform.GetComponent<T>();
                }
            }
            else
            {
                foreach (T component in go.GetComponentsInChildren<T>())
                {
                    if (string.IsNullOrEmpty(name) || component.name == name)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        // GameObject 자체를 반환하기 위해 사용
        private GameObject FindChild(GameObject go, string name = null, bool recursive = false)
        {
            Transform transform = FindChild<Transform>(go, name, recursive);
            if (transform != null)
            {
                return transform.gameObject;
            }
            return null;
        }
    }
}
