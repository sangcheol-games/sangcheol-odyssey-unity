using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

namespace SCOdyssey.App
{
    public readonly struct AudioID : IEquatable<AudioID>
    {
        public string AudioPath { get; }
        public AudioID(string audioPath) => AudioPath = audioPath ??
            throw new ArgumentNullException(nameof(audioPath));
 
        public bool Equals(AudioID other) => AudioPath == other.AudioPath;
        public override bool Equals(object obj) => obj is AudioID other && Equals(other);
        public override int GetHashCode() => AudioPath?.GetHashCode() ?? 0;
        public override string ToString() => AudioPath;
        public static bool operator==(AudioID lhs, AudioID rhs) =>  lhs.Equals(rhs);
        public static bool operator!=(AudioID lhs, AudioID rhs) => !lhs.Equals(rhs);
    }

    public readonly struct ScheduledEntry
    {
        public readonly AudioID Audio{ get; }
        public readonly double LocalTime{ get; }
        public bool Loop{ get; }

        public ScheduledEntry(in AudioID audio, in double localTime, in bool loop)
        {
            Audio = audio;
            LocalTime = localTime;
            Loop = loop;
        }

        public override string ToString()
        {
            return $"{Audio} {LocalTime} {Loop}";
        }
    }

    public sealed class AudioSession
    {
        public string Name{ get; }
        public readonly HashSet<AudioID> Audios;
        public IReadOnlyCollection<ScheduledEntry> ScheduledEvents{ get; }

        private AudioSession(in string name,
            in HashSet<AudioID> audios,
            in IReadOnlyCollection<ScheduledEntry> scheduledEvents
        ){
            Name = name;
            Audios = audios;
            ScheduledEvents = scheduledEvents;
        }
        public static Builder New(in string name = null) => new(name);

        public sealed class Builder
        {
            private readonly string _sessionName;
            private readonly HashSet<AudioID> _audios = new();
            private readonly HashSet<ScheduledEntry> _scheduled = new();

            internal Builder(in string sessionName){ _sessionName = sessionName; }

            public AudioSession Build()
            {
                return new AudioSession(_sessionName, _audios, _scheduled);
            }

            public Builder Define(in AudioID audio)
            {
                var added = _audios.Add(audio);
                if(!added) Debug.Log($"Already added on Session {_sessionName}: {audio}");

                return this;
            }

            public Builder Reserve(in AudioID audio,
                in double localTime = 0.0,
                in bool loop = false
            ){
                var schedule = new ScheduledEntry(audio, localTime, loop);
                var added = _scheduled.Add(schedule);
                if(!added) Debug.Log($"Already reserved on Session {_sessionName}: {schedule}");

                return this;
            }
        }
    }

    public class Owned<T>: IDisposable
        where T: class, IDisposable
    {
        private T _object;
        public T Object
        {
            get => _object;
            set
            {
                _object?.Dispose();
                _object = value;
            }
        }

        public void Reset(T next = null)
        {
            Object = next;
        }

        public void Dispose() => Reset();
    }

#region FMODResourceManager
    internal sealed class FMODResourceManager: IDisposable
    {
        private bool _disposed = false;
        private Dictionary<AudioID, FMOD.Sound> _sounds;

        // Debug Variables
        private FMOD.OUTPUTTYPE _soundBackend;
        private int _soundSampleRate;

        public void Dispose()
        {
            if(_disposed) return;

            foreach(var (_, sound) in _sounds)
            {
                if(sound.hasHandle()) sound.release();
            }

            _disposed = true;
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public void AssertBackendUnchanged(
            in FMOD.OUTPUTTYPE expectedBackend,
            in int expectedSampleRate
        ){
            Debug.Assert(
                expectedBackend == _soundBackend &&
                expectedSampleRate == _soundSampleRate,
                "[FMODResourceManager] FMOD Backed has been changed"
            );
        }
    }
#endregion

#region FMODAudioManager2
    public class FMODAudioManager2: MonoBehaviour, IDisposable
    {
        private bool _disposed = false;

        // cache FMOD CoreSystem
        private FMOD.System Sys;
        private FMOD.OUTPUTTYPE _sysBackend = FMOD.OUTPUTTYPE.UNKNOWN;
        private int _sysSampleRate;
        private FMOD.ChannelGroup _sysMasterGroup; // for global DSPClock

        private FMOD.ChannelGroup _masterGroup;
        private FMOD.ChannelGroup _bgmGroup;
        private FMOD.ChannelGroup _sfxGroup;

        private Owned<FMODResourceManager> _resource;
        private FMODResourceManager Resource{
            get => _resource.Object;
            set
            {
                _resource.Object = value;
            }
        }

        private void Awake()
        {
            Construct();
        }

        private void OnDestroy()
        {
            Destruct();
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertCoreSystemValid()
        {
            var currentSys = RuntimeManager.CoreSystem;

            Debug.Assert(
                currentSys.handle == Sys.handle,
                "[FMODAudioManager] System has been reset"
            );
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertCorePropertyValid()
        {
            AssertCoreSystemValid();

            Sys.getOutput(out var currentBackend);
            Sys.getSoftwareFormat(out var currentSamplerRate, out _, out _);

            Debug.Assert(
                currentBackend == _sysBackend &&
                currentSamplerRate == _sysSampleRate,
                "[FMODAudioManager] System has been reset, but cache wasn't changed"
            );
        }

        private void CacheCoreSystem()
        {
            Sys = RuntimeManager.CoreSystem;

            Sys.getOutput(out _sysBackend);
            Sys.getSoftwareFormat(out _sysSampleRate, out _, out _);

            Sys.getMasterChannelGroup(out _sysMasterGroup);
        }

        private void CreateChannelGroup()
        {
            AssertCoreSystemValid();

            Sys.createChannelGroup("Master", out _masterGroup);
            Sys.createChannelGroup("BGM", out _bgmGroup);
            Sys.createChannelGroup("SFX", out _sfxGroup);
            _masterGroup.addGroup(_bgmGroup, false, out _);
            _masterGroup.addGroup(_sfxGroup, false, out _);
        }

        private void Construct()
        {
            Debug.Assert(!_disposed);

            CacheCoreSystem();
            CreateChannelGroup();

            Resource = new FMODResourceManager();
        }

        private void Destruct()
        {
            Debug.Assert(!_disposed);

            Resource = null;

            _sfxGroup.release();
            _bgmGroup.release();
            _masterGroup.release();
        }

        public void Dispose()
        {
            if(_disposed) return;

            Destruct();

            _disposed = true;
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertBackendUnchanged()
        {
            AssertCorePropertyValid();

            Resource.AssertBackendUnchanged(_sysBackend, _sysSampleRate);
        }

        public void Schedule(in AudioSession session, in double startAt)
        {
            
        }

        public void Play(in AudioID audio)
        {
            
        }
    }
#endregion

    public class FMODAudioManagerUseCase
    {
        public void DefaultUsage()
        {
            var session = AudioSession.New("Song1 - Easy")
                .Define(new AudioID("HitSound"))
                .Reserve(new AudioID("Song1"), localTime: 0.0)
                .Reserve(new AudioID("CountDown"), localTime: -3.0)
                .Reserve(new AudioID("Clear"), localTime: 180)
                .Build();

            using var audioManager = new FMODAudioManager2();
            audioManager.Schedule(session, startAt: 0);

            audioManager.Play(new AudioID("Hit Sound"));
        }
    }
}