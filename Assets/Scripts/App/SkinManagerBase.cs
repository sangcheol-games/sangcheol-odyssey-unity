using System.Collections.Generic;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Core;
using SCOdyssey.Domain.Dto;
using SCOdyssey.Domain.Entity;
using UnityEngine;

namespace SCOdyssey.App
{
    public abstract class SkinManagerBase<T> : ISkinManager<T> where T : SkinSO
    {
        private const string PREFS_KEY = "SCOdyssey.SkinPrefs.v1";

        private readonly string _resourcePath;
        private List<T> _skinList;
        private T _userSelected;
        private int _savedId;
        private bool _isLoaded;

        protected SkinManagerBase(string resourcePath)
        {
            _resourcePath = resourcePath;
            // 생성자: id만 읽어둠 — Resources 로드 없음 (Lazy Loading)
            var json = PlayerPrefs.GetString(PREFS_KEY, "");
            var prefs = string.IsNullOrEmpty(json)
                ? new SkinPrefsData()
                : JsonAdapter.FromJson<SkinPrefsData>(json);
            _savedId = ReadId(prefs);
        }

        private void EnsureLoaded()
        {
            if (_isLoaded) return;
            _skinList = new List<T>(Resources.LoadAll<T>(_resourcePath));
            _userSelected = _skinList.Find(s => s.id == _savedId)
                ?? (_skinList.Count > 0 ? _skinList[0] : null);
            _isLoaded = true;
        }

        public T GetCurrentSkin()
        {
            EnsureLoaded();
            return _userSelected;
        }

        public List<T> GetSkinList()
        {
            EnsureLoaded();
            return _skinList;
        }

        public void SelectSkin(T skin)
        {
            EnsureLoaded();
            _userSelected = skin;

            // 현재 전체 prefs 로드 → 내 타입 id만 업데이트 → 저장 (다른 스킨 타입 값 보존)
            var json = PlayerPrefs.GetString(PREFS_KEY, "");
            var prefs = string.IsNullOrEmpty(json)
                ? new SkinPrefsData()
                : JsonAdapter.FromJson<SkinPrefsData>(json);
            WriteId(prefs, skin != null ? skin.id : -1);
            PlayerPrefs.SetString(PREFS_KEY, JsonAdapter.ToJson(prefs));
            PlayerPrefs.Save();
        }

        /// <summary>SkinPrefsData에서 이 스킨 타입의 id를 읽는다.</summary>
        protected abstract int ReadId(SkinPrefsData prefs);

        /// <summary>SkinPrefsData에 이 스킨 타입의 id를 쓴다.</summary>
        protected abstract void WriteId(SkinPrefsData prefs, int id);
    }
}
