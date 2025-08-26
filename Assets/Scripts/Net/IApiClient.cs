using System.Threading.Tasks;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.Net
{
    public interface IApiClient
    {
        // session based login
        Task<AuthUrlResponse> SessionInit(string codeVerifier);
        Task<AuthTokens> SessionPoll(string sessionId);
        Task Logout();

        // token
        Task<AuthTokens> Refresh(string refreshToken);

        // general API
        Task<string> Ping();

        // user API
        Task<UserMe> GetMe(string accessToken = null);
        Task<UserMe> SetNickname(string nickname);
        Task<LinkOut> LinkIdentity(string provider, string sub, object claims = null);
        Task<UnlinkOut> UnlinkIdentity(string provider);
    }
}
