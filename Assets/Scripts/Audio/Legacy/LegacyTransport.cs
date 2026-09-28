using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Audio.Output;
using SCOdyssey.Audio.Playback;
using SCOdyssey.Core;
using UnityEngine;

namespace SCOdyssey.Audio.Legacy
{
    // [과도기] ILegacyTransport 구현. 음악 슬롯 하나를 SCO.Music 아래에 두고, 옛 FMODAudioManager와 같은 순서로 예약한다
    // (playSound(paused) → setDelay → setLoopCount → setPriority(0) → unpause). 로드 결과는 옛 코드처럼 IsLoaded만 알린다.
    //
    // DSP 초(DspSeconds) = 기준 초 + (SCO.Music DSP 클록 - 기준 클록) / 레이트.
    //   재구성 직전(OnEngineClosing)에 기준 초를 지금 값으로, 직후(OnEngineOpened)에 기준 클록을 새 System 클록으로 둔다.
    //   그래서 세대가 바뀌어도 값이 이어지고, 옛 GameManager의 globalStartTime 산술이 깨지지 않는다.
    //   엔진을 쓸 수 없으면(Degraded·Failed) QPC로 진행한다.
    // 재구성 때는 슬롯 상태(파일, 반복, 위치, 일시정지, 아직 시작 전인 예약)를 기억해 두고, 다시 열리면 이어서 재생한다.
    // 위치는 ms 단위라 샘플 단위로 맞지는 않는다(과도기 한계, I1에서 새 곡 세션으로 옮기면 없어진다).
    internal sealed class LegacyTransport : ILegacyTransport
    {
        private const int MusicPriority = 0;
        private const double RestoreTimeoutSeconds = 3.0;

        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly OneShotBank _oneShots;
        private readonly DeviceCatalog _catalog;
        private readonly IAudioOutputService _output;
        private readonly string _folder;

        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private string _file;
        private bool _loop;
        private bool _loaded;
        private bool _loading;
        private double _scheduledAt = double.NaN;

        // DSP 초
        private bool _usesQpc = true;
        private double _baseSeconds;
        private ulong _baseClock;
        private int _rate;
        private long _qpcOrigin = Qpc.Now;
        private double _lastSeconds;

        // 재구성 뒤 되살리기
        private bool _restorePending;
        private bool _restorePlaying;
        private bool _restorePaused;
        private uint _restorePositionMs;
        private double _restoreStartAt = double.NaN;
        private bool _restoreSeeking;
        private long _restoreStartedQpc;

        public LegacyTransport(AudioEngine engine, FmodMixer mixer, OneShotBank oneShots, DeviceCatalog catalog, IAudioOutputService output, string folder)
        {
            _engine = engine;
            _mixer = mixer;
            _oneShots = oneShots;
            _catalog = catalog;
            _output = output;
            _folder = folder;
        }

        public bool IsLoaded
        {
            get { return _loaded; }
        }

        public bool IsPlaying
        {
            get { return ChannelAlive(); }
        }

        public double DspSeconds
        {
            get
            {
                double value = _lastSeconds;
                if (_usesQpc)
                {
                    value = _baseSeconds + Qpc.ToSeconds(Qpc.Now - _qpcOrigin);
                }
                else if (_mixer.MusicGroup.getDSPClock(out ulong clock, out _) == FMOD.RESULT.OK && clock >= _baseClock)
                {
                    value = _baseSeconds + (clock - _baseClock) / (double)_rate;
                }
                if (value < _lastSeconds) value = _lastSeconds;
                _lastSeconds = value;
                return value;
            }
        }

        public void Load(string fileName, bool loop)
        {
            AudioThread.AssertMain("ILegacyTransport.Load");
            ReleaseSlot();
            _restorePending = false;
            _file = fileName;
            _loop = loop;
            StartLoad();
        }

        public void PlayAt(double dspSeconds, bool loop)
        {
            AudioThread.AssertMain("ILegacyTransport.PlayAt");
            if (!_loaded)
            {
                Debug.LogError("[Audio] (과도기) PlayScheduled: 오디오가 로드되지 않았습니다.");
                return;
            }
            _loop = loop;
            _scheduledAt = dspSeconds;
            if (_usesQpc || !_sound.hasHandle()) return;

            ReleaseChannel();
            FMOD.RESULT result = _engine.CoreSystem.playSound(_sound, _mixer.MusicGroup, true, out _channel);
            if (result != FMOD.RESULT.OK)
            {
                Debug.LogError("[Audio] (과도기) playSound 실패: " + result);
                _channel = default;
                return;
            }
            _channel.setDelay(ToClock(dspSeconds), 0, false);
            ApplyChannelDefaults();
            _channel.setPaused(false);
        }

        // 재구성 뒤 되살리는 중(되감기·위치 이동 대기)에도 마지막 요청이 이기도록 일시정지 여부를 기억한다.
        public void Pause()
        {
            if (ChannelAlive()) _channel.setPaused(true);
            _restorePaused = true;
        }

        public void Resume()
        {
            if (ChannelAlive()) _channel.setPaused(false);
            _restorePaused = false;
        }

        public void Stop()
        {
            ReleaseChannel();
            _scheduledAt = double.NaN;
            _restorePending = false;
            _restoreSeeking = false;
        }

        public int RegisterOneShot(string fileName)
        {
            OneShotId id = _oneShots.Register(fileName);
            if (!id.IsValid) return -1;
            return id.Slot - 1;
        }

        public void PlayOneShot(int slot)
        {
            if (slot < 0) return;
            _oneShots.Play(new OneShotId(slot + 1));
        }

        public string[] GetDeviceNames()
        {
            IReadOnlyList<AudioDeviceInfo> devices = ReadDevices();
            var names = new string[devices.Count];
            for (int i = 0; i < devices.Count; i++) names[i] = devices[i].Name;
            return names;
        }

        public void SelectDevice(int index)
        {
            IReadOnlyList<AudioDeviceInfo> devices = ReadDevices();
            if (index < 0 || index >= devices.Count) return;
            AudioOutputInfo current = _engine.CurrentOutput;
            AudioOutputKind kind = CurrentKind();
            BootAttempt attempt = _engine.CurrentAttempt;
            var request = new AudioOutputRequest(kind, devices[index].Id, devices[index].Name, attempt.BufferLength, attempt.BufferCount);
            ApplyAndLog(request, current.DeviceName).Forget();
        }

        // 매 프레임(AudioModule.Tick).
        internal void Tick()
        {
            if (_loading) PollLoad();
            if (_restoreSeeking) StepRestore();
        }

        internal void OnEngineClosing()
        {
            double now = DspSeconds;
            _baseSeconds = now;

            bool hadSlot = _sound.hasHandle() || _loading;
            _restorePending = hadSlot && !string.IsNullOrEmpty(_file);
            _restorePlaying = false;
            _restoreStartAt = double.NaN;
            _restoreSeeking = false;
            if (_restorePending && IsPlaying)
            {
                _restorePlaying = true;
                _channel.getPaused(out _restorePaused);
                _channel.getPosition(out _restorePositionMs, FMOD.TIMEUNIT.MS);
                // 아직 소리가 시작되지 않은 예약이면 같은 DSP 초로 다시 예약한다.
                if (!double.IsNaN(_scheduledAt) && _scheduledAt > now) _restoreStartAt = _scheduledAt;
            }
            ReleaseSlot();
        }

        // 처음 부팅한 뒤와 재구성 뒤에 부른다(믹서가 만들어진 다음).
        internal void OnEngineOpened()
        {
            _baseSeconds = _lastSeconds;
            _usesQpc = true;
            if (_engine.IsUsable && _mixer.IsBuilt && _engine.CurrentOutput.SampleRate > 0
                && _mixer.MusicGroup.getDSPClock(out ulong clock, out _) == FMOD.RESULT.OK)
            {
                _rate = _engine.CurrentOutput.SampleRate;
                _baseClock = clock;
                _usesQpc = false;
            }
            _qpcOrigin = Qpc.Now;

            if (_restorePending) StartLoad();
        }

        internal void Shutdown()
        {
            ReleaseSlot();
            _restorePending = false;
        }

        private void StartLoad()
        {
            _loaded = false;
            _loading = false;
            if (!_engine.IsUsable)
            {
                // 엔진을 쓸 수 없으면 기다리는 쪽(GameDataLoader 등)이 멈추지 않게 곧바로 로드된 것으로 둔다.
                _loaded = true;
                _restorePending = false;
                return;
            }

            string path = Path.Combine(_folder, _file);
            FMOD.MODE mode = FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING;
            if (_loop) mode |= FMOD.MODE.LOOP_NORMAL;
            FMOD.RESULT result = _engine.CoreSystem.createSound(path, mode, out _sound);
            if (result != FMOD.RESULT.OK)
            {
                _sound = default;
                _restorePending = false;
                Debug.LogError("[Audio] (과도기) createSound 실패: " + result + " | 경로: " + path);
                return;
            }
            _loading = true;
        }

        private void PollLoad()
        {
            StreamLoader.Phase phase = StreamLoader.Poll(_sound);
            if (phase == StreamLoader.Phase.Opening) return;
            _loading = false;
            if (phase == StreamLoader.Phase.Failed)
            {
                _restorePending = false;
                Debug.LogError("[Audio] (과도기) 오디오 로드 실패: " + _file);
                return;
            }
            _loaded = true;
            if (_restorePending) BeginRestore();
        }

        private void BeginRestore()
        {
            _restorePending = false;
            if (!double.IsNaN(_restoreStartAt))
            {
                bool paused = _restorePaused;
                PlayAt(_restoreStartAt, _loop);
                if (paused) Pause();
                return;
            }
            if (!_restorePlaying) return;

            FMOD.RESULT result = _engine.CoreSystem.playSound(_sound, _mixer.MusicGroup, true, out _channel);
            if (result != FMOD.RESULT.OK)
            {
                _channel = default;
                return;
            }
            ApplyChannelDefaults();
            _restoreSeeking = true;
            _restoreStartedQpc = Qpc.Now;
        }

        // NONBLOCKING 스트림은 playSound 뒤 비동기로 되감으므로, 되감기가 끝난 뒤 위치를 옮기고 재생한다.
        private void StepRestore()
        {
            bool timedOut = Qpc.ToSeconds(Qpc.Now - _restoreStartedQpc) > RestoreTimeoutSeconds;
            if (!timedOut)
            {
                if (!StreamLoader.IsPlayable(_sound)) return;
                FMOD.RESULT result = _channel.setPosition(_restorePositionMs, FMOD.TIMEUNIT.MS);
                if (result == FMOD.RESULT.ERR_NOTREADY) return;
            }
            _restoreSeeking = false;
            if (!_restorePaused) _channel.setPaused(false);
        }

        private void ApplyChannelDefaults()
        {
            ChannelEndWatch.Watch(_channel);
            if (_loop) _channel.setLoopCount(-1);
            else _channel.setLoopCount(0);
            // BGM은 타격음이 몰려도 보이스 스틸링 대상이 되면 안 된다.
            _channel.setPriority(MusicPriority);
        }

        private ulong ToClock(double dspSeconds)
        {
            double clock = _baseClock + (dspSeconds - _baseSeconds) * _rate;
            if (clock < 0) return 0;
            return (ulong)Math.Round(clock);
        }

        private IReadOnlyList<AudioDeviceInfo> ReadDevices()
        {
            AudioOutputKind kind = CurrentKind();
            IReadOnlyList<AudioDeviceInfo> devices = _catalog.GetCached(kind);
            if (devices.Count == 0) devices = _catalog.Enumerate(kind);
            return devices;
        }

        private AudioOutputKind CurrentKind()
        {
            AudioOutputKind kind = _engine.CurrentOutput.Kind;
            if (kind == AudioOutputKind.NoSound) return AudioOutputKind.Wasapi;
            return kind;
        }

        private async UniTaskVoid ApplyAndLog(AudioOutputRequest request, string previousDevice)
        {
            AudioApplyResult result = await _output.ApplyAsync(request, CancellationToken.None);
            Debug.Log("[Audio] (과도기) 장치 변경 " + previousDevice + " → " + request.DeviceName + ": " + result.Outcome + " " + result.Message);
        }

        // 옛 GameManager·ChartManager는 곡이 끝난 뒤에도 매 프레임 IsPlaying을 읽는다. 끝난 채널 핸들을 부르면
        // FMOD가 오류 콜백(ERR_INVALID_HANDLE)을 내므로, END 콜백이 온 채널은 부르지 않고 버린다.
        private bool ChannelAlive()
        {
            if (!_channel.hasHandle()) return false;
            if (ChannelEndWatch.HasEnded(_channel))
            {
                _channel = default;
                return false;
            }
            FMOD.RESULT result = _channel.isPlaying(out bool playing);
            if (result == FMOD.RESULT.OK && playing) return true;
            _channel = default;
            return false;
        }

        private void ReleaseChannel()
        {
            if (ChannelAlive()) _channel.stop();
            _channel = default;
        }

        private void ReleaseSlot()
        {
            ReleaseChannel();
            if (_sound.hasHandle()) _sound.release();
            _sound = default;
            _loaded = false;
            _loading = false;
            _scheduledAt = double.NaN;
        }
    }
}
