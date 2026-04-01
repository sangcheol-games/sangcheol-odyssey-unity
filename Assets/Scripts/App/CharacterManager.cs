using SCOdyssey.App.Interfaces;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App
{
    public class CharacterManager : SkinManagerBase<CharacterSO>, ICharacterManager
    {
        public CharacterManager() : base("Characters") { }
    }
}
