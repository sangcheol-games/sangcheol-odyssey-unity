using System;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.App
{
    public interface ISettingsManager
    {
        SettingsData Current { get; }

        void Load();
        void Save();
        void Apply();
        void ResetToDefault();

        event Action<SettingsData> OnSettingsChanged;
    }
}
