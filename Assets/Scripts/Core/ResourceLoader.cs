using UnityEngine;

namespace SCOdyssey.Core
{
    public static class ResourceLoader
    {
        // Load & Prefabs 경로 생략
        public static GameObject PrefabInstantiate(string path, Transform parent = null)
        {
            GameObject prefab = Resources.Load<GameObject>($"Prefabs/{path}");

            if (prefab == null)
            {
                Debug.Log($"Failed to load prefab : {path}");
                return null;
            }

            // 프리팹 생성 후, 이름 뒤 (Clone) 삭제
            GameObject go = Object.Instantiate(prefab, parent);
            go.name = prefab.name;

            return go;
        }
    }
}

