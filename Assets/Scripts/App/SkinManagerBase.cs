using System.Collections.Generic;
using UnityEngine;
using SCOdyssey.App.Interfaces;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App
{
    public abstract class SkinManagerBase<T> : ISkinManager<T> where T : SkinSO
    {
        protected List<T> _skinList;
        protected T _userSelected;

        private readonly string _prefsKey;

        protected SkinManagerBase(string resourcePath)
        {
            _prefsKey = $"SelectedSkin_{typeof(T).Name}";
            _skinList = new List<T>(Resources.LoadAll<T>(resourcePath));

            // PlayerPrefs에서 마지막 선택 복원
            if (PlayerPrefs.HasKey(_prefsKey))
            {
                int savedId = PlayerPrefs.GetInt(_prefsKey);
                _userSelected = _skinList.Find(s => s.id == savedId);
            }

            // 저장된 값이 없거나 매칭되는 스킨이 없으면 첫 번째로 fallback
            if (_userSelected == null && _skinList.Count > 0)
                _userSelected = _skinList[0];
        }

        public void SelectSkin(T skin)
        {
            _userSelected = skin;
            if (skin != null)
                PlayerPrefs.SetInt(_prefsKey, skin.id);
        }

        public T GetCurrentSkin() => _userSelected;

        public List<T> GetSkinList() => _skinList;
    }
}
