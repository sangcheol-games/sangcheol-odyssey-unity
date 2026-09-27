using System.Threading;
using Cysharp.Threading.Tasks;

namespace SCOdyssey.Audio
{
    public interface IMusicPlayer
    {
        bool IsPlaying { get; }
        UniTask<AudioLoadResult> PlayAsync(string fileName, bool loop, CancellationToken ct);   // 마지막 요청만 유효
        void Stop();
    }

    public interface IMusicPlayers
    {
        IMusicPlayer Lobby { get; }
        IMusicPlayer Preview { get; }
    }
}
