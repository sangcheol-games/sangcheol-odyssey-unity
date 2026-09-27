using System.Threading;
using Cysharp.Threading.Tasks;

namespace SCOdyssey.Audio
{
    public readonly struct SongLoadResult
    {
        public readonly AudioLoadStatus Status;
        public readonly ISongSession Session;   // Ok일 때만 있다
        public readonly string Detail;

        public SongLoadResult(AudioLoadStatus status, ISongSession session, string detail)
        {
            Status = status;
            Session = session;
            Detail = detail;
        }

        public bool Ok
        {
            get { return Status == AudioLoadStatus.Ok; }
        }
    }

    public interface ISongPlayer
    {
        ISongSession Current { get; }
        UniTask<SongLoadResult> LoadAsync(string fileName, CancellationToken ct);   // 이전 세션은 Dispose된다
    }
}
