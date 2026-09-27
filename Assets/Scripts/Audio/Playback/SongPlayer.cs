using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SCOdyssey.Audio.Clock;
using SCOdyssey.Audio.Engine;
using SCOdyssey.Audio.Mixing;
using SCOdyssey.Core;
using UnityEngine.SceneManagement;

namespace SCOdyssey.Audio.Playback
{
    // 게임 곡 로드와 세션 수명. 세션은 로드한 씬이 언로드되거나 다음 로드가 오면 Dispose된다.
    // 엔진을 쓸 수 없으면(Degraded·Failed) 파일이 있을 때 무음 세션을 돌려준다(곡 시계는 QPC로 진행).
    internal sealed class SongPlayer : ISongPlayer
    {
        private readonly AudioEngine _engine;
        private readonly FmodMixer _mixer;
        private readonly ClockSampler _sampler;
        private readonly string _folder;
        private readonly CancellationToken _moduleToken;
        private FmodSongSession _current;

        public SongPlayer(AudioEngine engine, FmodMixer mixer, ClockSampler sampler, string folder, CancellationToken moduleToken)
        {
            _engine = engine;
            _mixer = mixer;
            _sampler = sampler;
            _folder = folder;
            _moduleToken = moduleToken;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public ISongSession Current
        {
            get { return _current; }
        }

        internal FmodSongSession CurrentInternal
        {
            get { return _current; }
        }

        public async UniTask<SongLoadResult> LoadAsync(string fileName, CancellationToken ct)
        {
            AudioThread.AssertMain("ISongPlayer.LoadAsync");
            DisposeCurrent();
            if (_moduleToken.IsCancellationRequested) return new SongLoadResult(AudioLoadStatus.Cancelled, null, "모듈 종료");
            if (string.IsNullOrEmpty(fileName)) return new SongLoadResult(AudioLoadStatus.NotFound, null, "파일명이 비어 있음");

            string path = Path.Combine(_folder, fileName);
            if (!File.Exists(path)) return new SongLoadResult(AudioLoadStatus.NotFound, null, path);

            var session = new FmodSongSession(_engine, _mixer, _sampler, path, SceneManager.GetActiveScene().handle);
            _current = session;

            // 열기 진행은 모듈 Tick이 한다. 여기서는 끝나기를 기다리기만 한다.
            long started = Qpc.Now;
            while (session.IsOpening)
            {
                if (Qpc.ToSeconds(Qpc.Now - started) > StreamLoader.OpenTimeoutSeconds)
                {
                    DisposeIfCurrent(session);
                    return new SongLoadResult(AudioLoadStatus.Timeout, null, path);
                }
                try
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                catch (OperationCanceledException)
                {
                    DisposeIfCurrent(session);
                    return new SongLoadResult(AudioLoadStatus.Cancelled, null, path);
                }
                if (_moduleToken.IsCancellationRequested || _current != session || session.State == SongSessionState.Disposed)
                {
                    return new SongLoadResult(AudioLoadStatus.Superseded, null, path);
                }
            }

            if (session.OpenFailed)
            {
                string detail = session.OpenError + ": " + path;
                DisposeIfCurrent(session);
                return new SongLoadResult(AudioLoadStatus.DecodeError, null, detail);
            }
            return new SongLoadResult(AudioLoadStatus.Ok, session, path);
        }

        internal void Tick()
        {
            if (_current == null) return;
            _current.Tick();
            if (_current.State == SongSessionState.Disposed) _current = null;
        }

        internal void OnEngineClosing()
        {
            if (_current != null) _current.OnEngineClosing();
        }

        internal void OnEngineOpened()
        {
            if (_current != null) _current.OnEngineOpened();
        }

        internal void Shutdown()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            DisposeCurrent();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_current != null && _current.OwnerScene == scene.handle) DisposeCurrent();
        }

        private void DisposeIfCurrent(FmodSongSession session)
        {
            session.Dispose();
            if (_current == session) _current = null;
        }

        private void DisposeCurrent()
        {
            if (_current == null) return;
            FmodSongSession session = _current;
            _current = null;
            session.Dispose();
        }
    }
}
