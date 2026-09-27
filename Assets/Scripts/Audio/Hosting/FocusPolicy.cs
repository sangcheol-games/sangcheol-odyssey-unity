using System;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Playback;
using UnityEngine;

namespace SCOdyssey.Audio.Hosting
{
    // 포커스 정책.
    //   - 게임 곡이 흐르는 중(Starting, LeadIn, Playing, Resuming)에 포커스를 잃으면 설정과 관계없이 일시정지한다.
    //   - 포커스를 잃고 백그라운드 재생이 꺼져 있으면 SCO.Master를 음소거한다(로비).
    internal sealed class FocusPolicy
    {
        private readonly FmodMixer _mixer;
        private readonly SongPlayer _songPlayer;
        private readonly Func<bool> _playInBackground;
        private readonly bool _pauseSongOnFocusLoss;

        public FocusPolicy(FmodMixer mixer, SongPlayer songPlayer, Func<bool> playInBackground, bool pauseSongOnFocusLoss)
        {
            _mixer = mixer;
            _songPlayer = songPlayer;
            _playInBackground = playInBackground;
            _pauseSongOnFocusLoss = pauseSongOnFocusLoss;
        }

        public void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus && _pauseSongOnFocusLoss)
            {
                ISongSession session = _songPlayer.Current;
                if (session != null) session.Pause(PauseReason.FocusLost);   // 흐르는 상태가 아니면 Pause가 무시한다
            }

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
