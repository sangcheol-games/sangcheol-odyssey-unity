using System;
using System.Collections.Generic;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Config
{
    // 설정 SO 찾는 순서: Inspector 지정 -> ServiceLocator 등록본 -> Resources.
    // 전부 없으면 null을 돌려주고 타입당 한 번만 경고한다. 기본값은 호출자가 고른다.
    public static class ConfigLocator
    {
        private static readonly HashSet<Type> _warned = new();

        public static T Resolve<T>(T serialized, string resourcePath) where T : ScriptableObject
        {
            if (serialized != null) return serialized;
            if (ServiceLocator.TryGet(out T registered) && registered != null) return registered;

            T loaded = Resources.Load<T>(resourcePath);
            if (loaded != null) return loaded;

            if (_warned.Add(typeof(T)))
                Debug.LogWarning($"[ConfigLocator] {typeof(T).Name} 없음 (Resources/{resourcePath}). 코드 기본값을 쓴다.");
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarnings() => _warned.Clear();
    }
}
