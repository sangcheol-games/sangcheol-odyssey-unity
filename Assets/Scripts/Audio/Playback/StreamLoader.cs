namespace SCOdyssey.Audio.Playback
{
    // NONBLOCKING 스트림의 열기 상태 판정.
    internal static class StreamLoader
    {
        public const double OpenTimeoutSeconds = 10.0;

        public enum Phase
        {
            Opening,
            Ready,
            Failed
        }

        public static Phase Poll(FMOD.Sound sound)
        {
            if (!sound.hasHandle()) return Phase.Failed;
            // 해제된 핸들은 state가 기본값(READY)으로 오므로 결과 코드로 먼저 거른다.
            FMOD.RESULT result = sound.getOpenState(out FMOD.OPENSTATE state, out _, out _, out _);
            if (result == FMOD.RESULT.ERR_INVALID_HANDLE) return Phase.Failed;
            if (state == FMOD.OPENSTATE.ERROR) return Phase.Failed;
            if (state == FMOD.OPENSTATE.READY || state == FMOD.OPENSTATE.PLAYING) return Phase.Ready;
            return Phase.Opening;
        }

        // 채널에 물린 스트림은 READY가 아니라 PLAYING이다. 열기·seek·버퍼링 중이 아니고 굶주리지 않으면 재생할 수 있다.
        // playSound 직후에는 처음으로 되감는 비동기 seek(SETPOSITION)가 돌므로 이 값이 false다.
        public static bool IsPlayable(FMOD.Sound sound)
        {
            if (!sound.hasHandle()) return false;
            FMOD.RESULT result = sound.getOpenState(out FMOD.OPENSTATE state, out _, out bool starving, out _);
            if (result == FMOD.RESULT.ERR_INVALID_HANDLE) return false;
            bool opened = state == FMOD.OPENSTATE.READY || state == FMOD.OPENSTATE.PLAYING;
            return opened && !starving;
        }
    }
}
