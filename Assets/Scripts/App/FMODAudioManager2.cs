using System;
using System.Collections.Generic;
using System.Diagnostics;
using FMODUnity;
using Debug = UnityEngine.Debug;

namespace SCOdyssey.App
{
    public readonly struct AudioID : IEquatable<AudioID>
    {
        public string AudioPath { get; }
        public AudioID(string audioPath) => AudioPath = audioPath ??
            throw new ArgumentNullException(nameof(audioPath));
 
        public override string ToString() => AudioPath;

        public bool Equals(AudioID other) => AudioPath == other.AudioPath;
        public override bool Equals(object obj) => obj is AudioID other && Equals(other);
        public override int GetHashCode() => AudioPath?.GetHashCode() ?? 0;
        public static bool operator==(AudioID lhs, AudioID rhs) =>  lhs.Equals(rhs);
        public static bool operator!=(AudioID lhs, AudioID rhs) => !lhs.Equals(rhs);
    }

    public readonly struct ScheduledEntry : IEquatable<ScheduledEntry>
    {
        public readonly AudioID Audio{ get; }
        public readonly double LocalTime{ get; }
        public readonly bool Loop{ get; }

        public ScheduledEntry(AudioID audio, double localTime, bool loop)
        {
            Audio = audio;
            LocalTime = localTime;
            Loop = loop;
        }

        public override string ToString()
        {
            return $"{Audio} {LocalTime} {Loop}";
        }

        public bool Equals(ScheduledEntry other)
        {
            return Audio == other.Audio &&
                LocalTime == other.LocalTime &&
                Loop == other.Loop;
        }
        public override bool Equals(object obj) => obj is ScheduledEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Audio, LocalTime, Loop);
        public static bool operator==(ScheduledEntry lhs, ScheduledEntry rhs) => lhs.Equals(rhs);
        public static bool operator!=(ScheduledEntry lhs, ScheduledEntry rhs) => !lhs.Equals(rhs);
    }

    public sealed class AudioSession
    {
        public string Name{ get; }
        public IReadOnlyCollection<AudioID> Audios{ get; }
        public IReadOnlyCollection<ScheduledEntry> ScheduledEvents{ get; }

        private AudioSession(string name,
            in HashSet<AudioID> audios,
            in IReadOnlyCollection<ScheduledEntry> scheduledEvents
        ){
            Name = name;
            Audios = audios;
            ScheduledEvents = scheduledEvents;
        }
        public static Builder New(string name = null) => new(name);

        public sealed class Builder
        {
            private string _sessionName;
            private HashSet<AudioID> _audios = new();
            private HashSet<ScheduledEntry> _scheduled = new();

            internal Builder(string sessionName){ _sessionName = sessionName; }

            public AudioSession Build()
            {
                var session = new AudioSession(_sessionName, _audios, _scheduled);

                _sessionName = null;
                _audios = null;
                _scheduled = null;

                return session;
            }

            public Builder Define(AudioID audio)
            {
                var added = _audios.Add(audio);
                if(!added) Debug.Log($"Already added on Session {_sessionName}: {audio}");

                return this;
            }

            public Builder Reserve(AudioID audio,
                double localTime = 0.0,
                bool loop = false
            ){
                var schedule = new ScheduledEntry(audio, localTime, loop);
                var added = _scheduled.Add(schedule);
                if(!added) Debug.Log($"Already reserved on Session {_sessionName}: {schedule}");

                return this;
            }
        }
    }

#region ResourceManager
    internal sealed class FMODResourceManager: IDisposable
    {
        private Dictionary<AudioID, FMOD.Sound> _sounds = new();

        // Debug Variables
        private FMOD.OUTPUTTYPE _soundBackend = FMOD.OUTPUTTYPE.UNKNOWN;
        private int _soundSampleRate = 0;

        public FMODResourceManager(FMOD.OUTPUTTYPE backend, int sampleRate)
        {
            // Initialize Default Settings
            _soundBackend = backend;
            _soundSampleRate = sampleRate;
        }

        public void Dispose()
        {
            foreach(var (_, sound) in _sounds)
            {
                if(sound.hasHandle()){
                    sound.release();
                    sound.clearHandle();
                }
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public void AssertBackendUnchanged(
            FMOD.OUTPUTTYPE expectedBackend,
            int expectedSampleRate
        ){
            Debug.Assert(
                expectedBackend == _soundBackend &&
                expectedSampleRate == _soundSampleRate,
                "[FMODResourceManager] FMOD Backed has been changed"
            );
        }
    }
#endregion

#region AudioManager
    public class FMODAudioManager2: IDisposable
    {
        private bool _disposed = false;
        // cache FMOD CoreSystem
        private FMOD.System Sys;
        private FMOD.OUTPUTTYPE _sysBackend = FMOD.OUTPUTTYPE.UNKNOWN;
        private int _sysSampleRate = 0;
        private FMOD.ChannelGroup _sysMasterGroup; // for global DSPClock

        private FMOD.ChannelGroup _masterGroup;
        private FMOD.ChannelGroup _bgmGroup;
        private FMOD.ChannelGroup _sfxGroup;

        private FMODResourceManager _resourceManager = null;
        private FMODResourceManager ResourceManager
        {
            get{ return _resourceManager; }
            set
            {
                _resourceManager?.Dispose();
                _resourceManager = value;
            }
        }

        public FMODAudioManager2()
        {
            CacheCoreSystem();
            CreateChannelGroup();

            ResourceManager = new FMODResourceManager(_sysBackend, _sysSampleRate);
        }

        ~FMODAudioManager2()
        {
            Debug.LogError("[FMODAudioManager2] Disposed by GC. Owner forgot to call Dispose()");
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

            var result = Sys.getOutput(out var currentBackend);
            CheckFMODResult(result, "AssertCoreProperty, Get Current System Backend");
            result = Sys.getSoftwareFormat(out var currentSamplerRate, out _, out _);
            CheckFMODResult(result, "AssertCoreProperty, Get Current System SampleRate");

            Debug.Assert(
                currentBackend == _sysBackend &&
                currentSamplerRate == _sysSampleRate,
                "[FMODAudioManager] System has been reset, but cache wasn't changed"
            );
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private static void CheckFMODResult(FMOD.RESULT result, string context = null)
        {
            if(result != FMOD.RESULT.OK)
                Debug.LogError($"FMOD result: {result} ({context})");
        }

        private void CacheCoreSystem()
        {
            Sys = RuntimeManager.CoreSystem;

            var result = Sys.getOutput(out _sysBackend);
            CheckFMODResult(result, "CacheCoreSystem, Get System Backend");
            result = Sys.getSoftwareFormat(out _sysSampleRate, out _, out _);
            CheckFMODResult(result, "CacheCoreSystem, Get System SampleRate");

            result = Sys.getMasterChannelGroup(out _sysMasterGroup);
            CheckFMODResult(result, "CacheCoreSystem, Get System Master Group");
        }

        private void CreateChannelGroup()
        {
            AssertCoreSystemValid();

            var result = Sys.createChannelGroup("Master", out _masterGroup);
            CheckFMODResult(result, "CreateChannelGroup, Create Master Group");
            result = Sys.createChannelGroup("BGM", out _bgmGroup);
            CheckFMODResult(result, "CreateChannelGroup, Create BGM Group");
            result = Sys.createChannelGroup("SFX", out _sfxGroup);
            CheckFMODResult(result, "CreateChannelGroup, Create SFX Group");
            result = _masterGroup.addGroup(_bgmGroup, false, out _);
            CheckFMODResult(result, "CreateChannelGroup, Register BGM Group to Master Group");
            result = _masterGroup.addGroup(_sfxGroup, false, out _);
            CheckFMODResult(result, "CreateChannelGroup, Register SFX Group to Master Group");
        }

        public void Dispose()
        {
            if(_disposed) return;
            _disposed = true;

            ResourceManager = null;

            if (_sfxGroup.hasHandle())
            {
                var result = _sfxGroup.release();
                CheckFMODResult(result, "Dispose, SFX Group Release");
                _sfxGroup.clearHandle();
            }
            if (_bgmGroup.hasHandle())
            {
                var result = _bgmGroup.release();
                CheckFMODResult(result, "Dispose, BGM Group Release");
                _bgmGroup.clearHandle();
            }
            if (_masterGroup.hasHandle())
            {
                var result = _masterGroup.release();
                CheckFMODResult(result, "Dispose, Master Group Release");
                _masterGroup.clearHandle();
            }

            GC.SuppressFinalize(this);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertBackendUnchanged()
        {
            AssertCorePropertyValid();

            ResourceManager.AssertBackendUnchanged(_sysBackend, _sysSampleRate);
        }

        public void Schedule(AudioSession session, double startAt)
        {
            AssertBackendUnchanged();
        }

        public void Play(AudioID audio)
        {
            AssertBackendUnchanged();
        }
    }
#endregion

    internal class FMODAudioManagerUseCase
    {
        public void DefaultUsage()
        {
            var session = AudioSession.New("Song1 - Easy")
                .Define(new AudioID("HitSound"))
                .Reserve(new AudioID("Song1"), localTime: 0.0)
                .Reserve(new AudioID("CountDown"), localTime: -3.0)
                .Reserve(new AudioID("Clear"), localTime: 180)
                .Build();

            var audioManager = new FMODAudioManager2();

            audioManager.Schedule(session, startAt: 0);

            audioManager.Play(new AudioID("Hit Sound"));

            // OnDestroy
            audioManager.Dispose();
        }
    }
}