using System.Threading.Tasks;
using SCOdyssey.Net;

namespace SCOdyssey.Testing.Net
{
    public sealed class TestNicknameUpdateItem : ITestOfflineItem
    {
        private readonly IApiClient _api;
        public string IdempotencyKey { get; }
        public int Attempts { get; set; }
        private readonly string _nickname;

        public TestNicknameUpdateItem(IApiClient api, string nickname, string key)
        {
            _api = api;
            _nickname = nickname;
            IdempotencyKey = key;
        }

        public async Task<bool> TrySendAsync()
        {
            await _api.SetNickname(_nickname);
            return true;
        }
    }
}
