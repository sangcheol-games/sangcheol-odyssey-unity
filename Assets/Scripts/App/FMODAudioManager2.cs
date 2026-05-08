using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SCOdyssey.App
{
    public readonly struct AudioID : IEquatable<AudioID>
    {
        public string AudioPath { get; }
        public AudioID(string audioPath) => AudioPath = audioPath ??
            throw new ArgumentNullException(nameof(audioPath));
 
        public string FullPath => System.IO.Path.Combine(Application.streamingAssetsPath, "Music", AudioPath);

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
        public IReadOnlyCollection<AudioID> SFXAudios{ get; }
        public IReadOnlyCollection<ScheduledEntry> ScheduledBGM{ get; }

        private AudioSession(string name,
            in HashSet<AudioID> SFXAudios,
            in IReadOnlyCollection<ScheduledEntry> scheduledBGM
        ){
            Name = name;
            this.SFXAudios = SFXAudios;
            ScheduledBGM = scheduledBGM;
        }
        public static Builder New(string name = null) => new(name);

        public sealed class Builder
        {
            private string _sessionName;
            private HashSet<AudioID> _SFXAudios = new();
            private HashSet<ScheduledEntry> _scheduledBGM = new();

            internal Builder(string sessionName){ _sessionName = sessionName; }

            public AudioSession Build()
            {
                var session = new AudioSession(_sessionName, _SFXAudios, _scheduledBGM);

                _sessionName = null;
                _SFXAudios = null;
                _scheduledBGM = null;

                return session;
            }

            public Builder DefineSFX(AudioID audio)
            {
                var added = _SFXAudios.Add(audio);
                if(!added) Debug.Log($"Already added on Session {_sessionName}: {audio}");

                return this;
            }

            public Builder ReserveBGM(AudioID audio,
                double localTime = 0.0,
                bool loop = false
            ){
                var schedule = new ScheduledEntry(audio, localTime, loop);
                var added = _scheduledBGM.Add(schedule);
                if(!added) Debug.Log($"Already reserved on Session {_sessionName}: {schedule}");

                return this;
            }
        }
    }

    internal enum SessionState
    {
        Created,
        Loading,
        Ready,    // Load Completed, Ready for Play
        Playing,  // On Schedule
        Ending,   // StopAll invoked, wait for dispose
        Disposed
    }

    internal sealed class ActiveAudioSession : IDisposable
    {
        private bool _disposed = false;
        public AudioSession Spec{ get; }
        public SessionState State{ get; private set; } = SessionState.Created;

        private FMOD.System Sys{ get; }
        private FMOD.ChannelGroup _BGMGroup, _SFXGroup;

        private Dictionary<AudioID, FMOD.Sound> _sounds = new();
        private Dictionary<ScheduledEntry, FMOD.Channel> _scheduledChannels = new();
        private readonly HashSet<AudioID> _BGMAudios = new();

        public ActiveAudioSession(
            FMOD.System sys,
            FMOD.ChannelGroup BGMParent,
            FMOD.ChannelGroup SFXParent,
            AudioSession spec
        )
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));

            Sys = sys;

            foreach(var entry in spec.ScheduledBGM)
            {
                _BGMAudios.Add(entry.Audio);
            }

            var prefix = string.IsNullOrEmpty(spec.Name) ? "Anonymous" : spec.Name;
            var BGMGroupName = $"{prefix}_BGM";
            var SFXGroupName = $"{prefix}_SFX";

            // Create Session Channel Group
            var result = Sys.createChannelGroup(BGMGroupName, out _BGMGroup);
            FMODUtil.CheckFMODResult(result, $"Create ${BGMGroupName}");
            result = Sys.createChannelGroup(SFXGroupName, out _SFXGroup);
            FMODUtil.CheckFMODResult(result, $"Create ${SFXGroupName}");

            result = BGMParent.addGroup(_BGMGroup, false);
            FMODUtil.CheckFMODResult(result, $"Attach ${BGMGroupName}");
            result = SFXParent.addGroup(_SFXGroup, false);
            FMODUtil.CheckFMODResult(result, $"Attach ${SFXGroupName}");
        }

        ~ActiveAudioSession()
        {
            Debug.LogError($"[{nameof(ActiveAudioSession)}] Disposed by GC. Owner forgot to call Dispose(): {Spec?.Name}");
        }

        private bool CheckSoundState((AudioID, FMOD.Sound) tuple)
        {
            var (id, sound) = tuple;

            var result = sound.getOpenState(out var openState, out _, out _, out _);
            FMODUtil.CheckFMODResult(result, $"getOpenState: {id}");

             if (openState == FMOD.OPENSTATE.READY)
            {
                _sounds[id] = sound;
                return true;
            }
            else if (openState == FMOD.OPENSTATE.ERROR)
            {
                Debug.LogError($"[{nameof(ActiveAudioSession)}] Failed to load: {id}");
                sound.release();
                return true;
            }

            return false;
        }

        public IEnumerator LoadAsync()
        {
            if (State != SessionState.Created)
            {
                Debug.LogError($"[{nameof(ActiveAudioSession)}] LoadAsync invalid state: {State}");
                yield break;
            }

            State = SessionState.Loading;

            const FMOD.MODE BGM_MODE = 
                FMOD.MODE.IGNORETAGS | FMOD.MODE.NONBLOCKING |
                FMOD.MODE.ACCURATETIME | FMOD.MODE.CREATESAMPLE |
                FMOD.MODE.LOOP_OFF;
            const FMOD.MODE SFX_MODE = 
                FMOD.MODE.NONBLOCKING | FMOD.MODE.CREATESAMPLE |
                FMOD.MODE.LOOP_OFF;

            var pending = new List<(AudioID, FMOD.Sound)>(Spec.SFXAudios.Count);
            foreach (var audio in Spec.SFXAudios)
            {
                FMOD.MODE mode = SFX_MODE;
                if (_BGMAudios.Contains(audio))
                    mode = BGM_MODE;

                var path = audio.FullPath;
                var result = Sys.createSound(path, mode, out var sound);
                FMODUtil.CheckFMODResult(result, $"createSound: {audio.AudioPath}");
                pending.Add((audio, sound));
            }

            while (pending.Count > 0)
            {
                pending.RemoveAll(CheckSoundState);

                yield return null;
            }

            State = SessionState.Ready;
        }

        public void Schedule(ulong startSample, int sampleRate)
        {
            if (State != SessionState.Ready)
            {
                Debug.LogError($"[{nameof(ActiveAudioSession)}] Schedule invalid state: {State}");
                return;
            }

            foreach (var entry in Spec.ScheduledBGM)
            {
                if(!_sounds.TryGetValue(entry.Audio, out var sound))
                {
                    Debug.LogError($"[{nameof(ActiveAudioSession)}] Sound not loaded: {entry.Audio}");
                    continue;
                }

                // start with paused
                var result = Sys.playSound(sound, _BGMGroup, true, out FMOD.Channel channel);
                FMODUtil.CheckFMODResult(result, $"playSound: {entry}");

                ulong entryStart = startSample + (ulong)(entry.LocalTime * sampleRate);
                result = channel.setDelay(entryStart, 0, false);
                FMODUtil.CheckFMODResult(result, $"setDelay: {entry}");

                if (entry.Loop)
                {
                    result = channel.setMode(FMOD.MODE.LOOP_NORMAL);
                    FMODUtil.CheckFMODResult(result, $"setMode loop: {entry}");
                }

                result = channel.setPaused(false);
                FMODUtil.CheckFMODResult(result, $"setPaused: {entry}");

                _scheduledChannels[entry] = channel;
            }

            State = SessionState.Playing;
        }

        public void Play(AudioID audio)
        {
            if (State != SessionState.Ready && State != SessionState.Playing)
            {
                Debug.LogError($"[{nameof(ActiveAudioSession)}] Play invalid state: {State}");
                return;
            }

            if (!_sounds.TryGetValue(audio, out var sound))
            {
                Debug.LogError($"[{nameof(ActiveAudioSession)}] Sound not loaded: {audio}");
                return;
            }

            // Immediate Play
            var result = Sys.playSound(sound, _SFXGroup, false, out _);
            FMODUtil.CheckFMODResult(result, $"playSound SFX: {audio}");
        }

        // Check for Current ActiveAudioSession is safe to Dispise
        public bool IsIdle()
        {
            if(!_BGMGroup.hasHandle() || !_SFXGroup.hasHandle()) return true;

            var result = _BGMGroup.getNumChannels(out int BGMCount);
            FMODUtil.CheckFMODResult(result, "getNumChannels BGM");
            result = _SFXGroup.getNumChannels(out int SFXCount);
            FMODUtil.CheckFMODResult(result, "getNumChannels SFX");

            return BGMCount == 0 && SFXCount == 0;
        }

        public void StopAll()
        {
            if (_BGMGroup.hasHandle())
            {
                var result = _BGMGroup.stop();
                FMODUtil.CheckFMODResult(result, "stop BGM subgroup");
            }
            if (_SFXGroup.hasHandle())
            {
                var result = _SFXGroup.stop();
                FMODUtil.CheckFMODResult(result, "stop SFX subgroup");
            }
            _scheduledChannels.Clear();
            State = SessionState.Ending;
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var (_, sound) in _sounds)
            {
                if (sound.hasHandle())
                {
                    sound.release();
                    sound.clearHandle();
                }
            }
            _sounds.Clear();
            _scheduledChannels.Clear();

            if (_SFXGroup.hasHandle())
            {
                var result = _SFXGroup.release();
                FMODUtil.CheckFMODResult(result, "release SFX subgroup");
                _SFXGroup.clearHandle();
            }
            if (_BGMGroup.hasHandle())
            {
                var result = _BGMGroup.release();
                FMODUtil.CheckFMODResult(result, "release BGM subgroup");
                _BGMGroup.clearHandle();
            }

            State = SessionState.Disposed;
            GC.SuppressFinalize(this);
        }
    }

    internal static class FMODUtil
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        internal static void CheckFMODResult(FMOD.RESULT result, string context = null)
        {
            if(result != FMOD.RESULT.OK)
                Debug.LogError($"FMOD result: {result} ({context})");
        }
    }

#region AudioManager
    public class FMODAudioManager2: IDisposable
    {
        private bool _disposed = false;
        // cache FMOD CoreSystem
        private FMOD.System Sys;
        private FMOD.OUTPUTTYPE _sysBackend = FMOD.OUTPUTTYPE.UNKNOWN;
        private int _sysSampleRate = 0;
        private FMOD.ChannelGroup _sysMasterGroup; // for global DSPClock
        private ulong DSPClock{
            get{
                var result = _sysMasterGroup.getDSPClock(out ulong clock, out _);
                FMODUtil.CheckFMODResult(result, $"[{nameof(FMODAudioManager2)}] DSPClock");

                return clock;
            }
        }
        private double DSPClockSecond => (double)DSPClock / _sysSampleRate;

        private FMOD.ChannelGroup _masterGroup, _BGMGroup, _SFXGroup;

        private readonly List<ActiveAudioSession> _activeSessions = new();
        private readonly Dictionary<AudioID, ActiveAudioSession> _audioToSession = new();
        private readonly Dictionary<string, ActiveAudioSession> _sessionByName = new();
        private readonly List<ActiveAudioSession> _pendingDispose = new();


        public FMODAudioManager2()
        {
            CacheCoreSystem();
            CreateChannelGroup();
        }

        ~FMODAudioManager2()
        {
            Debug.LogError($"[{nameof(FMODAudioManager2)}] Disposed by GC. Owner forgot to call Dispose()");
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertCoreSystemValid()
        {
            var currentSys = RuntimeManager.CoreSystem;

            Debug.Assert(
                currentSys.handle == Sys.handle,
                $"[{nameof(FMODAudioManager2)}] System has been reset"
            );
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void AssertCorePropertyValid()
        {
            AssertCoreSystemValid();

            var result = Sys.getOutput(out var currentBackend);
            FMODUtil.CheckFMODResult(result, $"{nameof(AssertCorePropertyValid)}, Get Current System Backend");
            result = Sys.getSoftwareFormat(out var currentSamplerRate, out _, out _);
            FMODUtil.CheckFMODResult(result, $"{nameof(AssertCorePropertyValid)}, Get Current System SampleRate");

            Debug.Assert(
                currentBackend == _sysBackend &&
                currentSamplerRate == _sysSampleRate,
                $"[{nameof(FMODAudioManager2)}] System has been reset, but cache stale"
            );
        }

        private void CacheCoreSystem()
        {
            Sys = RuntimeManager.CoreSystem;

            var result = Sys.getOutput(out _sysBackend);
            FMODUtil.CheckFMODResult(result, $"{nameof(CacheCoreSystem)}, Get System Backend");
            result = Sys.getSoftwareFormat(out _sysSampleRate, out _, out _);
            FMODUtil.CheckFMODResult(result, $"{nameof(CacheCoreSystem)}, Get System SampleRate");

            result = Sys.getMasterChannelGroup(out _sysMasterGroup);
            FMODUtil.CheckFMODResult(result, $"{nameof(CacheCoreSystem)}, Get System Master Group");
        }

        private void CreateChannelGroup()
        {
            AssertCoreSystemValid();

            var result = Sys.createChannelGroup("Master", out _masterGroup);
            FMODUtil.CheckFMODResult(result, $"{nameof(CreateChannelGroup)}, Create Master Group");
            result = Sys.createChannelGroup("BGM", out _BGMGroup);
            FMODUtil.CheckFMODResult(result, $"{nameof(CreateChannelGroup)}, Create BGM Group");
            result = Sys.createChannelGroup("SFX", out _SFXGroup);
            FMODUtil.CheckFMODResult(result, $"{nameof(CreateChannelGroup)}, Create SFX Group");
            result = _masterGroup.addGroup(_BGMGroup, false, out _);
            FMODUtil.CheckFMODResult(result, $"{nameof(CreateChannelGroup)}, Register BGM Group to Master Group");
            result = _masterGroup.addGroup(_SFXGroup, false, out _);
            FMODUtil.CheckFMODResult(result, $"{nameof(CreateChannelGroup)}, Register SFX Group to Master Group");
        }

        public IEnumerator PushSessionAsync(AudioSession session)
        {
            AssertCoreSystemValid();

            if (session == null || string.IsNullOrEmpty(session.Name))
            {
                Debug.LogError($"[{nameof(FMODAudioManager2)}] Invalid session spec");
                yield break;
            }
            if (_sessionByName.ContainsKey(session.Name))
            {
                Debug.LogError($"[{nameof(FMODAudioManager2)}] Session already pushed: {session.Name}");
                yield break;
            }

            foreach (var audio in session.SFXAudios)
            {
                if (_audioToSession.ContainsKey(audio))
                {
                    Debug.LogError(
                        $"[{nameof(FMODAudioManager2)}] Audio {audio} already in another session"
                    );
                    yield break;
                }
            }

            var activated = new ActiveAudioSession(Sys, _BGMGroup, _SFXGroup, session);
            foreach (var audio in session.SFXAudios)
                _audioToSession[audio] = activated;
            _sessionByName[session.Name] = activated;
            _activeSessions.Add(activated);

            yield return activated.LoadAsync();
        }

        public void PopSession(string name)
        {
            if (!_sessionByName.TryGetValue(name, out var session))
            {
                Debug.LogError($"[{nameof(FMODAudioManager2)}] Session not found: {name}");
                return;
            }

            session.StopAll();

            _sessionByName.Remove(name);
            _activeSessions.Remove(session);
            foreach (var audio in session.Spec.SFXAudios)
                _audioToSession.Remove(audio);

            _pendingDispose.Add(session);
        }

        public void Update()
        {
            foreach(var session in _pendingDispose)
            {
                if(session.IsIdle()) session.Dispose();
            }

            _pendingDispose.RemoveAll((session) => session.IsIdle());
        }

        public void Dispose()
        {
            if(_disposed) return;
            _disposed = true;

            foreach (var session in _activeSessions)
            {
                session.StopAll();
                session.Dispose();
            }
            foreach (var session in _pendingDispose)
                session.Dispose();

            _activeSessions.Clear();
            _pendingDispose.Clear();
            _audioToSession.Clear();
            _sessionByName.Clear();

            if (_SFXGroup.hasHandle())
            {
                var result = _SFXGroup.release();
                FMODUtil.CheckFMODResult(result, $"{nameof(Dispose)}, SFX Group Release");
                _SFXGroup.clearHandle();
            }
            if (_BGMGroup.hasHandle())
            {
                var result = _BGMGroup.release();
                FMODUtil.CheckFMODResult(result, $"{nameof(Dispose)}, BGM Group Release");
                _BGMGroup.clearHandle();
            }
            if (_masterGroup.hasHandle())
            {
                var result = _masterGroup.release();
                FMODUtil.CheckFMODResult(result, $"{nameof(Dispose)}, Master Group Release");
                _masterGroup.clearHandle();
            }

            GC.SuppressFinalize(this);
        }

        public async void Schedule(string sessionName, double startAt)
        {
            AssertCorePropertyValid();

            if(!_sessionByName.TryGetValue(sessionName, out var session))
            {
                
            }
        }

        public void Play(AudioID audio)
        {
            AssertCorePropertyValid();

            if(!_audioToSession.TryGetValue(audio, out var session))
            {
                Debug.LogError($"[{nameof(FMODAudioManager2)}] Audio not in any session: {audio}");
                return;
            }

            session.Play(audio);
        }

        public void SetMasterVolume(float v)
        {
            var result = _masterGroup.setVolume(v);
            FMODUtil.CheckFMODResult(result, nameof(SetMasterVolume));
        }
        public void SetBGMVolume(float v)
        {
            var result = _BGMGroup.setVolume(v);
            FMODUtil.CheckFMODResult(result, nameof(SetBGMVolume));
        }
        public void SetSFXVolume(float v)
        {
            var result = _SFXGroup.setVolume(v);
            FMODUtil.CheckFMODResult(result, nameof(SetSFXVolume));
        }
    }
#endregion

    internal class FMODAudioManagerUseCase
    {
        public void DefaultUsage()
        {
            var session = AudioSession.New("Song1 - Easy")
                .DefineSFX(new AudioID("HitSound"))
                .ReserveBGM(new AudioID("Song1"), localTime: 0.0)
                .ReserveBGM(new AudioID("CountDown"), localTime: -3.0)
                .ReserveBGM(new AudioID("Clear"), localTime: 180)
                .Build();

            var audioManager = new FMODAudioManager2();

            audioManager.Schedule("Song1 - Easy", startAt: 0);

            audioManager.Play(new AudioID("Hit Sound"));

            // OnDestroy
            audioManager.Dispose();
        }
    }
}