using System.Collections.Generic;
using UnityEngine;

namespace SCOdyssey.Game
{
    // 프리팹 하나의 오브젝트 풀. 여유분이 있으면 재사용하고 없으면 parent 아래에 새로 만든다.
    // 꺼낸 오브젝트의 활성화는 꺼낸 쪽이 한다. 반환하면 비활성화한 뒤 parent 아래로 옮긴다(로컬 값 유지)
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
            // 월드 행렬을 로컬 값에 굽지 않는다. 홀드바는 프리팹 localScale(0.5)을 그대로 읽어 쓴다
            go.transform.SetParent(_parent, false);
            _pool.Enqueue(go);
        }
    }
}
