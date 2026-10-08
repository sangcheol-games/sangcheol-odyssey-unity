using System;
using System.Collections.Generic;
using System.IO;
using SCOdyssey.Audio.Engine;
using UnityEngine;

namespace SCOdyssey.Audio.Playback
{
    // 원샷(타격음·UI 효과음 공용, 버스마다 인스턴스 하나). 파일명으로 멱등 등록하고, 재생은 playSound 한 번뿐이다(할당·로그·채널별 설정 없음).
    // CREATESAMPLE로 전체를 메모리에 디코드해 두므로 재생 시점에 디스크 I/O와 디코드가 없다.
    // 재구성 뒤에는 같은 슬롯에 다시 로드하므로 게임이 받아 둔 OneShotId가 그대로 유효하다.
    internal sealed class OneShotBank : ISfxPlayer
    {
        public const int Capacity = 64;
        public const int HitSoundPriority = 64;
        public const int SfxPriority = 128;     // 채널이 모자라면 타격음이 이긴다(숫자가 클수록 낮은 우선순위)
        private const FMOD.MODE LoadMode = FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D | FMOD.MODE.LOOP_OFF | FMOD.MODE.IGNORETAGS | FMOD.MODE.LOWMEM;

        private readonly string _folder;
        private readonly int _priority;
        private readonly string[] _names = new string[Capacity + 1];            // 슬롯 1부터 쓴다(0은 None)
        private readonly FMOD.Sound[] _sounds = new FMOD.Sound[Capacity + 1];
        private readonly Dictionary<string, int> _slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _count;

        private FMOD.System _system;
        private FMOD.ChannelGroup _group;

        public OneShotBank(string folder, int priority)
        {
            _folder = folder;
            _priority = priority;
        }

        public int Count
        {
            get { return _count; }
        }

        // 엔진이 Running이면 등록된 파일을 모두 (다시) 로드한다.
        public void Bind(AudioEngine engine, FMOD.ChannelGroup group)
        {
            Release();
            if (!engine.IsUsable) return;
            _system = engine.CoreSystem;
            _group = group;
            for (int slot = 1; slot <= _count; slot++)
            {
                string error = Load(slot);
                if (error != null) Debug.LogWarning("[Audio] 원샷 다시 로드 실패: " + _names[slot] + " (" + error + ")");
            }
        }

        public OneShotId Register(string fileName)
        {
            AudioThread.AssertMain("OneShotBank.Register");
            if (string.IsNullOrEmpty(fileName)) return OneShotId.None;

            int existing;
            if (_slots.TryGetValue(fileName, out existing)) return new OneShotId(existing);

            if (_count >= Capacity)
            {
                Debug.LogError("[Audio] 원샷 슬롯이 가득 찼습니다(" + Capacity + "): " + fileName);
                return OneShotId.None;
            }

            string path = Path.Combine(_folder, fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[Audio] 원샷 파일이 없습니다: " + path);
                return OneShotId.None;
            }

            int slot = _count + 1;
            _names[slot] = fileName;
            if (_system.hasHandle())
            {
                string error = Load(slot);
                if (error != null)
                {
                    _names[slot] = null;
                    Debug.LogWarning("[Audio] 원샷 로드 실패: " + path + " (" + error + ")");
                    return OneShotId.None;
                }
            }

            _count = slot;
            _slots[fileName] = slot;
            return new OneShotId(slot);
        }

        public void Play(OneShotId id)
        {
            int slot = id.Slot;
            if (slot <= 0 || slot > _count) return;
            FMOD.Sound sound = _sounds[slot];
            if (!sound.hasHandle()) return;
            _system.playSound(sound, _group, false, out _);
        }

        // 재구성·종료 때 부른다. 파일명과 슬롯 번호는 남는다.
        public void Release()
        {
            for (int slot = 1; slot <= _count; slot++)
            {
                if (_sounds[slot].hasHandle()) _sounds[slot].release();
                _sounds[slot] = default;
            }
            _system = default;
            _group = default;
        }

        private string Load(int slot)
        {
            string path = Path.Combine(_folder, _names[slot]);
            FMOD.RESULT result = _system.createSound(path, LoadMode, out FMOD.Sound sound);
            if (result != FMOD.RESULT.OK) return result.ToString();

            // 우선순위는 로드 때 기본값으로 둔다(재생 때 채널별 호출을 하지 않기 위해). 곡·음악 0, 타격음 64, 효과음 128.
            sound.getDefaults(out float frequency, out _);
            sound.setDefaults(frequency, _priority);
            _sounds[slot] = sound;
            return null;
        }
    }
}
