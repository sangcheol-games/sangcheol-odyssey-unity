using SCOdyssey.UI;
using UnityEngine;


namespace SCOdyssey.App
{
    // UI 진입 방식
    // Push    : 아래 UI를 숨기고 새 UI를 최상단에 추가 (기본값)
    // Overlay : 아래 UI를 보이는 채로 두고 위에 겹쳐 추가 (팝업)
    // Replace : 현재 최상단을 같은 깊이로 교체 (탭 전환 등, 뒤로가기 Depth를 늘리지 않음)
    public enum PushMode { Push, Overlay, Replace }

    public interface IUIManager
    {
        public T ShowUI<T>(PushMode mode = PushMode.Push) where T : BaseUI;
        public void CloseUI(BaseUI ui);
        
        // 입력 라우팅용: 현재 최상단 UI 반환 (없으면 null)
        public BaseUI PeekUI();
    }
}
