using SCOdyssey.Audio.Diagnostics;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Game.Timing;
using UnityEditor;

namespace SCOdyssey.EditorTools
{
    // 에디터 전용 오디오 메뉴.
    //   - ASIO 허용: ASIO는 프로세스당 하나라 에디터에서는 기본으로 막아 두고(AsioPolicy), 시험할 때만 켠다.
    //     바꾼 값은 다음 부팅(Play)이나 설정 적용부터 쓰인다.
    //   - 오버레이: Play 중 오디오 상태와 판정 타이밍(입력 배치, 판정 싱크 래치, 판정 오차 분포)을 화면 오른쪽 위에 그린다.
    //     도메인 리로드(Play 시작)마다 꺼진다.
    public static class AudioEditorMenu
    {
        private const string AllowAsioMenu = "SCOdyssey/Audio/에디터에서 ASIO 허용";
        private const string AudioOverlayMenu = "SCOdyssey/Audio/오디오 오버레이";
        private const string TimingOverlayMenu = "SCOdyssey/Audio/타이밍 오버레이";

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

        [MenuItem(AudioOverlayMenu)]
        private static void ToggleAudioOverlay()
        {
            AudioOverlay.Visible = !AudioOverlay.Visible;
        }

        [MenuItem(AudioOverlayMenu, true)]
        private static bool ToggleAudioOverlayValidate()
        {
            Menu.SetChecked(AudioOverlayMenu, AudioOverlay.Visible);
            return true;
        }

        [MenuItem(TimingOverlayMenu)]
        private static void ToggleTimingOverlay()
        {
            JudgementDriver.OverlayVisible = !JudgementDriver.OverlayVisible;
        }

        [MenuItem(TimingOverlayMenu, true)]
        private static bool ToggleTimingOverlayValidate()
        {
            Menu.SetChecked(TimingOverlayMenu, JudgementDriver.OverlayVisible);
            return true;
        }
    }
}
