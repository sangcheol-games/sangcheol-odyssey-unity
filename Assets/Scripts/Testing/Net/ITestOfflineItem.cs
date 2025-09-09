
using System.Threading.Tasks;

namespace SCOdyssey.Testing.Net
{
    public interface ITestOfflineItem
    {
        string IdempotencyKey { get; }
        int Attempts { get; set; }
        Task<bool> TrySendAsync();
    }
}