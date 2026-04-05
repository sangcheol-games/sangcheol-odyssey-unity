using System;

namespace SCOdyssey.Domain.Dto
{
    [Serializable]
    public class SkinPrefsData
    {
        public int characterId       = -1;  // -1 = 미설정 (첫 번째로 fallback)
        public int noteSkinId        = -1;
        public int backgroundThemeId = -1;
    }
}
