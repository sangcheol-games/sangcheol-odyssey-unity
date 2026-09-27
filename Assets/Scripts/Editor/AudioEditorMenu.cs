using SCOdyssey.Audio.Engine;
using UnityEditor;

namespace SCOdyssey.EditorTools
{
    // 에디터 전용 오디오 메뉴. ASIO는 프로세스당 하나라 에디터에서는 기본으로 막아 두고(AsioPolicy), 시험할 때만 켠다.
    // 바꾼 값은 다음 부팅(Play)이나 설정 적용부터 쓰인다.
    public static class AudioEditorMenu
    {
        private const string AllowAsioMenu = "SCOdyssey/Audio/에디터에서 ASIO 허용";

        [MenuItem(AllowAsioMenu)]
        private static void ToggleAllowAsio()
        {
            AsioPolicy.AllowInEditor = !AsioPolicy.AllowInEditor;
        }

        [MenuItem(AllowAsioMenu, true)]
        private static bool ToggleAllowAsioValidate()
        {
            Menu.SetChecked(AllowAsioMenu, AsioPolicy.AllowInEditor);
            return AsioPolicy.IsSupported;
        }
    }
}
