using System;
using SCOdyssey.Audio.Mixing;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 포커스 정책. 포커스를 잃고 백그라운드 재생이 꺼져 있으면 SCO.Master를 음소거한다.
    // 게임 중 자동 일시정지는 곡 세션이 들어오는 단계(S2a)에서 붙인다.
    internal sealed class FocusPolicy
    {
        private readonly FmodMixer _mixer;
        private readonly Func<bool> _playInBackground;

        public FocusPolicy(FmodMixer mixer, Func<bool> playInBackground)
        {
            _mixer = mixer;
            _playInBackground = playInBackground;
        }

        public void OnFocusChanged(bool hasFocus)
        {
            bool mute = !hasFocus && !ReadPlayInBackground();
            _mixer.SetFocusMuted(mute);
        }

        private bool ReadPlayInBackground()
        {
            if (_playInBackground == null) return true;
            try
            {
                return _playInBackground();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return true;
            }
        }
    }
}
