using SCOdyssey.App.Interfaces;
using SCOdyssey.Domain.Dto;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App
{
    public class CharacterManager : SkinManagerBase<CharacterSO>, ICharacterManager
    {
        public CharacterManager() : base("Characters") { }

        protected override int ReadId(SkinPrefsData prefs) => prefs.characterId;
        protected override void WriteId(SkinPrefsData prefs, int id) => prefs.characterId = id;
    }
}
