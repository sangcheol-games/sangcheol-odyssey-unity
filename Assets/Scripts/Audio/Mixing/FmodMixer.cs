using SCOdyssey.Audio.Engine;
using UnityEngine;

namespace SCOdyssey.Audio.Mixing
{
    // 버스 하나. 볼륨은 그룹이 없을 때(재구성 중, NOSOUND)도 기억했다가 그룹이 생기면 다시 넣는다.
    internal sealed class FmodMixBus : IMixBus
    {
        private float _volume = 1f;
        private FMOD.ChannelGroup _group;

        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = Mathf.Clamp01(value);
                if (_group.hasHandle()) _group.setVolume(_volume);
            }
        }

        public FMOD.ChannelGroup Group
        {
            get { return _group; }
        }

        public void Attach(FMOD.ChannelGroup group)
        {
            _group = group;
            _group.setVolume(_volume);
        }

        public void Detach()
        {
            _group = default;
        }
    }

    // ChannelGroup 트리. 모든 그룹은 DSP 클록을 전파하도록 연결하고, 어떤 그룹도 pause하지 않는다.
    //   System Master (곡 시계 기준 클록, 건드리지 않음)
    //   └─ SCO.Master (Master 볼륨, 로비 포커스 음소거)
    //      ├─ SCO.Music (BGM 볼륨)
    //      │   └─ SCO.Song (게임 곡 전용, pause·pitch 금지)
    //      ├─ SCO.HitSound
    //      └─ SCO.Sfx
    internal sealed class FmodMixer : IAudioMixer
    {
        private readonly FmodMixBus _master = new FmodMixBus();
        private readonly FmodMixBus _music = new FmodMixBus();
        private readonly FmodMixBus _hitSound = new FmodMixBus();
        private readonly FmodMixBus _sfx = new FmodMixBus();
        private FMOD.ChannelGroup _song;
        private bool _focusMuted;

        public IMixBus Master
        {
            get { return _master; }
        }

        public IMixBus Music
        {
            get { return _music; }
        }

        public IMixBus HitSound
        {
            get { return _hitSound; }
        }

        public IMixBus Sfx
        {
            get { return _sfx; }
        }

        public FMOD.ChannelGroup SongGroup
        {
            get { return _song; }
        }

        public FMOD.ChannelGroup MusicGroup
        {
            get { return _music.Group; }
        }

        public FMOD.ChannelGroup HitSoundGroup
        {
            get { return _hitSound.Group; }
        }

        public FMOD.ChannelGroup SfxGroup
        {
            get { return _sfx.Group; }
        }

        public bool IsBuilt
        {
            get { return _master.Group.hasHandle(); }
        }

        // 엔진이 Running일 때만 그룹을 만든다. 실패하면 만든 것을 되돌리고 사유를 돌려준다.
        public string Build(AudioEngine engine)
        {
            Release();
            if (!engine.IsUsable) return null;

            FMOD.System system = engine.CoreSystem;
            FMOD.ChannelGroup master;
            FMOD.ChannelGroup music;
            FMOD.ChannelGroup song;
            FMOD.ChannelGroup hitSound;
            FMOD.ChannelGroup sfx;

            FMOD.RESULT result = system.createChannelGroup("SCO.Master", out master);
            if (result != FMOD.RESULT.OK) return "SCO.Master: " + result;
            system.createChannelGroup("SCO.Music", out music);
            system.createChannelGroup("SCO.Song", out song);
            system.createChannelGroup("SCO.HitSound", out hitSound);
            result = system.createChannelGroup("SCO.Sfx", out sfx);
            if (result != FMOD.RESULT.OK)
            {
                ReleaseGroups(song, music, hitSound, sfx, master);
                return "ChannelGroup 생성: " + result;
            }

            result = engine.SystemMaster.addGroup(master, true);
            if (result == FMOD.RESULT.OK) result = master.addGroup(music, true);
            if (result == FMOD.RESULT.OK) result = music.addGroup(song, true);
            if (result == FMOD.RESULT.OK) result = master.addGroup(hitSound, true);
            if (result == FMOD.RESULT.OK) result = master.addGroup(sfx, true);
            if (result != FMOD.RESULT.OK)
            {
                ReleaseGroups(song, music, hitSound, sfx, master);
                return "addGroup: " + result;
            }

            _master.Attach(master);
            _music.Attach(music);
            _hitSound.Attach(hitSound);
            _sfx.Attach(sfx);
            _song = song;
            _master.Group.setMute(_focusMuted);
            return null;
        }

        // 재구성·종료 때 부른다. 볼륨과 음소거 상태는 남는다.
        public void Release()
        {
            ReleaseGroups(_song, _music.Group, _hitSound.Group, _sfx.Group, _master.Group);
            _song = default;
            _music.Detach();
            _hitSound.Detach();
            _sfx.Detach();
            _master.Detach();
        }

        // 볼륨과 별개인 음소거. 로비에서 포커스를 잃고 백그라운드 재생이 꺼져 있을 때 쓴다.
        public void SetFocusMuted(bool muted)
        {
            _focusMuted = muted;
            if (_master.Group.hasHandle()) _master.Group.setMute(muted);
        }

        private static void ReleaseGroups(params FMOD.ChannelGroup[] groups)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i].hasHandle()) groups[i].release();
            }
        }
    }
}
