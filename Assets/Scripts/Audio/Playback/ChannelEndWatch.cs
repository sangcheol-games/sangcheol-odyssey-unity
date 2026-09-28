using System;

namespace SCOdyssey.Audio.Playback
{
    // 끝난 채널 알아채기. 끝난(또는 빼앗긴) 채널 핸들에 isPlaying·stop을 부르면 FMOD가 오류 콜백(ERR_INVALID_HANDLE)을 내므로,
    // 채널 END 콜백이 남긴 핸들을 기억해 두고 그 채널은 부르지 않는다.
    // END 콜백은 System.update 안(메인 스레드, AudioEngineRunner -1010)에서 오므로 잠금이 필요 없다.
    internal static class ChannelEndWatch
    {
        private const int Capacity = 16;

        private static readonly FMOD.CHANNELCONTROL_CALLBACK s_callback = OnChannelCallback;
        private static readonly IntPtr[] s_ended = new IntPtr[Capacity];
        private static int s_next;

        // playSound 직후 부른다. 새 채널이 예전에 끝난 채널과 같은 핸들 값을 받았을 수 있으므로 그 기록은 지운다.
        public static void Watch(FMOD.Channel channel)
        {
            if (!channel.hasHandle()) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_ended[i] == channel.handle) s_ended[i] = IntPtr.Zero;
            }
            channel.setCallback(s_callback);
        }

        public static bool HasEnded(FMOD.Channel channel)
        {
            if (!channel.hasHandle()) return true;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_ended[i] == channel.handle) return true;
            }
            return false;
        }

        // 끝나지 않은 채널만 멈춘다.
        public static void StopIfAlive(FMOD.Channel channel)
        {
            if (!HasEnded(channel)) channel.stop();
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.CHANNELCONTROL_CALLBACK))]
        private static FMOD.RESULT OnChannelCallback(IntPtr control, FMOD.CHANNELCONTROL_TYPE controlType, FMOD.CHANNELCONTROL_CALLBACK_TYPE callbackType, IntPtr data1, IntPtr data2)
        {
            if (callbackType != FMOD.CHANNELCONTROL_CALLBACK_TYPE.END) return FMOD.RESULT.OK;
            s_ended[s_next] = control;
            s_next = (s_next + 1) % Capacity;
            return FMOD.RESULT.OK;
        }
    }
}
