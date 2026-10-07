using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Core;

namespace SCOdyssey.Audio.Playback
{
    // 로비 BGM·곡 선택 프리뷰 재생기. 마지막 요청만 유효하다(앞선 요청은 Superseded로 끝나고 자기 Sound를 해제한다).
    // 프리뷰는 디바운스 뒤에 연다(곡 목록을 빠르게 넘길 때 여는 비용과 소리 겹침을 막는다).
    // 재구성 뒤에는 재생 중이던 파일을 처음부터 다시 튼다.
    internal sealed class FmodMusicPlayer : IMusicPlayer
    {
        private const int MusicPriority = 0;

        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly string _folder;
        private readonly double _debounceSeconds;
        private readonly CancellationToken _moduleToken;

        private int _request;
        private FMOD.Sound _loadingSound;       // 열리는 중인 Sound. System close 전에 반드시 해제해야 한다
        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private string _file;
        private bool _loop;
        private bool _wantsPlaying;

        public FmodMusicPlayer(AudioEngine engine, FmodMixer mixer, string folder, double debounceSeconds, CancellationToken moduleToken)
        {
            _engine = engine;
            _mixer = mixer;
            _folder = folder;
            _debounceSeconds = debounceSeconds;
            _moduleToken = moduleToken;
        }

        public bool IsPlaying
        {
            get
            {
                if (!_channel.hasHandle() || ChannelEndWatch.HasEnded(_channel)) return false;
                if (_channel.isPlaying(out bool playing) != FMOD.RESULT.OK) return false;
                return playing;
            }
        }

        public async UniTask<AudioLoadResult> PlayAsync(string fileName, bool loop, CancellationToken ct)
        {
            AudioThread.AssertMain("IMusicPlayer.PlayAsync");
            int request = ++_request;
            ReleaseCurrent();
            // 앞선 요청이 열던 Sound는 여기서 해제한다(그 요청은 다음 확인에서 Superseded로 끝난다).
            if (_loadingSound.hasHandle()) _loadingSound.release();
            _loadingSound = default;
            _file = fileName;
            _loop = loop;
            _wantsPlaying = true;

            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _moduleToken))
            {
                if (_debounceSeconds > 0)
                {
                    try
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(_debounceSeconds), true, PlayerLoopTiming.Update, linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return new AudioLoadResult(AudioLoadStatus.Cancelled, fileName);
                    }
                    if (request != _request) return new AudioLoadResult(AudioLoadStatus.Superseded, fileName);
                }

                if (!_engine.IsUsable) return new AudioLoadResult(AudioLoadStatus.EngineUnavailable, fileName);
                if (string.IsNullOrEmpty(fileName)) return new AudioLoadResult(AudioLoadStatus.NotFound, "파일명이 비어 있음");
                string path = Path.Combine(_folder, fileName);
                if (!File.Exists(path)) return new AudioLoadResult(AudioLoadStatus.NotFound, path);

                FMOD.MODE mode = FMOD.MODE.CREATESTREAM | FMOD.MODE.NONBLOCKING | FMOD.MODE.IGNORETAGS;
                if (loop) mode |= FMOD.MODE.LOOP_NORMAL;
                else mode |= FMOD.MODE.LOOP_OFF;

                int generation = _engine.Generation;
                FMOD.RESULT result = _engine.CoreSystem.createSound(path, mode, out FMOD.Sound sound);
                if (result != FMOD.RESULT.OK) return new AudioLoadResult(AudioLoadStatus.DecodeError, result + ": " + path);
                _loadingSound = sound;

                long started = Qpc.Now;
                while (true)
                {
                    StreamLoader.Phase phase = StreamLoader.Poll(sound);
                    if (phase == StreamLoader.Phase.Ready) break;
                    if (phase == StreamLoader.Phase.Failed)
                    {
                        ReleaseLoading(sound);
                        return new AudioLoadResult(AudioLoadStatus.DecodeError, path);
                    }
                    if (Qpc.ToSeconds(Qpc.Now - started) > StreamLoader.OpenTimeoutSeconds)
                    {
                        ReleaseLoading(sound);
                        return new AudioLoadResult(AudioLoadStatus.Timeout, path);
                    }

                    try
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        ReleaseLoading(sound);
                        return new AudioLoadResult(AudioLoadStatus.Cancelled, path);
                    }
                    // 기다리는 동안 새 요청, Stop, 재구성(세대 변경)이 오면 이 요청은 버린다.
                    // 재구성이면 OnEngineClosing이 이미 해제했다.
                    if (request != _request || generation != _engine.Generation)
                    {
                        ReleaseLoading(sound);
                        return new AudioLoadResult(AudioLoadStatus.Superseded, path);
                    }
                }

                _loadingSound = default;
                sound.getDefaults(out float frequency, out _);
                sound.setDefaults(frequency, MusicPriority);
                result = _engine.CoreSystem.playSound(sound, _mixer.MusicGroup, false, out FMOD.Channel channel);
                if (result != FMOD.RESULT.OK)
                {
                    sound.release();
                    return new AudioLoadResult(AudioLoadStatus.DecodeError, "playSound " + result + ": " + path);
                }
                ChannelEndWatch.Watch(channel);
                _sound = sound;
                _channel = channel;
                return new AudioLoadResult(AudioLoadStatus.Ok, path);
            }
        }

        public void Stop()
        {
            AudioThread.AssertMain("IMusicPlayer.Stop");
            _request++;
            _wantsPlaying = false;
            ReleaseCurrent();
        }

        internal void OnEngineClosing()
        {
            _request++;
            ReleaseCurrent();
            if (_loadingSound.hasHandle()) _loadingSound.release();
            _loadingSound = default;
        }

        internal void OnEngineOpened()
        {
            if (!_wantsPlaying || string.IsNullOrEmpty(_file)) return;
            PlayAsync(_file, _loop, CancellationToken.None).Forget();
        }

        internal void Shutdown()
        {
            _request++;
            _wantsPlaying = false;
            ReleaseCurrent();
            if (_loadingSound.hasHandle()) _loadingSound.release();
            _loadingSound = default;
        }

        // 이 요청이 연 Sound를 해제한다. 재구성·종료 경로가 먼저 해제했으면(_loadingSound가 비었으면) 건드리지 않는다.
        private void ReleaseLoading(FMOD.Sound sound)
        {
            if (!_loadingSound.hasHandle()) return;
            if (!SameHandle(_loadingSound, sound)) return;
            _loadingSound.release();
            _loadingSound = default;
        }

        private static bool SameHandle(FMOD.Sound a, FMOD.Sound b)
        {
            return a.handle == b.handle;
        }

        private void ReleaseCurrent()
        {
            if (_channel.hasHandle()) ChannelEndWatch.StopIfAlive(_channel);
            if (_sound.hasHandle()) _sound.release();
            _channel = default;
            _sound = default;
        }
    }

    internal sealed class MusicPlayers : IMusicPlayers
    {
        public const double PreviewDebounceSeconds = 0.15;

        public MusicPlayers(FmodMusicPlayer lobby, FmodMusicPlayer preview)
        {
            LobbyPlayer = lobby;
            PreviewPlayer = preview;
        }

        public IMusicPlayer Lobby
        {
            get { return LobbyPlayer; }
        }

        public IMusicPlayer Preview
        {
            get { return PreviewPlayer; }
        }

        internal FmodMusicPlayer LobbyPlayer { get; }
        internal FmodMusicPlayer PreviewPlayer { get; }

        internal void OnEngineClosing()
        {
            LobbyPlayer.OnEngineClosing();
            PreviewPlayer.OnEngineClosing();
        }

        internal void OnEngineOpened()
        {
            LobbyPlayer.OnEngineOpened();
            PreviewPlayer.OnEngineOpened();
        }

        internal void Shutdown()
        {
            LobbyPlayer.Shutdown();
            PreviewPlayer.Shutdown();
        }
    }
}
