using SCOdyssey.UI;
using UnityEngine;


namespace SCOdyssey.App
{
    public interface IUIManager
    {
        public T ShowUI<T>(string name = null, Transform parent = null) where T : BaseUI;
        public void CloseUI(BaseUI ui);
    }
}
