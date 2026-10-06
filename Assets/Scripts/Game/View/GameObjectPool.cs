using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.Game
{
    // 프리팹 하나의 오브젝트 풀. 여유분이 있으면 재사용하고 없으면 parent 아래에 새로 만든다.
    // 꺼낸 오브젝트의 활성화는 꺼낸 쪽이 한다. 반환하면 비활성화한 뒤 parent 아래로 옮긴다(월드 위치 유지)
    public sealed class GameObjectPool
    {
        private readonly Queue<GameObject> _pool = new();
        private readonly GameObject _prefab;
        private readonly Transform _parent;

        public GameObjectPool(GameObject prefab, Transform parent)
        {
            _prefab = prefab;
            _parent = parent;
        }

        public GameObject Get() => _pool.Count > 0 ? _pool.Dequeue() : Object.Instantiate(_prefab, _parent);

        public void Return(GameObject go)
        {
            go.SetActive(false);
            go.transform.SetParent(_parent);
            _pool.Enqueue(go);
        }
    }
}
