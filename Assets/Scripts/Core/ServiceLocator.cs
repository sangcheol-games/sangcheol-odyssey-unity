using System;
using System.Collections.Generic;

namespace SCOdyssey.Core
{
    // 전역 싱글톤 Instance 패턴 대신, ServiceLocator를 통해 관리/조회
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _map = new();
        public static void Register<T>(T inst) => _map[typeof(T)] = inst;
        public static T Get<T>() => (T)_map[typeof(T)];
        public static bool TryGet<T>(out T inst)
        {
            if (_map.TryGetValue(typeof(T), out var o)) { inst = (T)o; return true; }
            inst = default; return false;
        }
    }
}
