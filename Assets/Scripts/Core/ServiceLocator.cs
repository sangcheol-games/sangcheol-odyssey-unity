using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SCOdyssey.Core
{
    // 전역 싱글톤 Instance 패턴 대신, ServiceLocator를 통해 관리/조회
    public static class ServiceLocator
    {
        private static readonly ReaderWriterLockSlim _lock = new();
        private static Dictionary<Type, object> _map = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _lock.EnterWriteLock();
            try { _map = new(); }
            finally { _lock.ExitWriteLock(); }
        }

        public static void Register<T>(T inst)
        {
            if (inst == null) throw new ArgumentNullException(nameof(inst));
            _lock.EnterWriteLock();
            try { _map[typeof(T)] = inst; }
            finally { _lock.ExitWriteLock(); }
        }

        public static bool TryRegister<T>(T inst)
        {
            if (inst == null) return false;
            var t = typeof(T);
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_map.ContainsKey(t)) return false;
                _lock.EnterWriteLock();
                try { _map[t] = inst; return true; }
                finally { _lock.ExitWriteLock(); }
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        public static bool TryGet<T>(out T inst)
        {
            _lock.EnterReadLock();
            try
            {
                if (_map.TryGetValue(typeof(T), out var o)) { inst = (T)o; return true; }
                inst = default; return false;
            }
            finally { _lock.ExitReadLock(); }
        }

        public static T Get<T>() => TryGet<T>(out var v) ? v : throw new KeyNotFoundException(typeof(T).Name);

        public static bool Remove<T>()
        {
            _lock.EnterWriteLock();
            try { return _map.Remove(typeof(T)); }
            finally { _lock.ExitWriteLock(); }
        }
    }
}
