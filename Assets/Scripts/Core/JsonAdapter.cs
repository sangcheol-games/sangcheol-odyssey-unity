using System;
using UnityEngine;

namespace SCOdyssey.Core
{
    public static class JsonAdapter
    {
        public static string ToJson<T>(T obj, bool pretty=false) => JsonUtility.ToJson(obj, pretty);
        public static T FromJson<T>(string json) => JsonUtility.FromJson<T>(json);
    }
}
