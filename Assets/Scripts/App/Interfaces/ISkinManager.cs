using System.Collections.Generic;
using SCOdyssey.Domain.Entity;

namespace SCOdyssey.App.Interfaces
{
    public interface ISkinManager<T> where T : SkinSO
    {
        void SelectSkin(T skin);
        T GetCurrentSkin();
        List<T> GetSkinList();
    }
}
