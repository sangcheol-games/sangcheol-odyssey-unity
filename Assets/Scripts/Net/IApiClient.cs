using System.Threading.Tasks;
using SCOdyssey.Domain.Dto;

namespace SCOdyssey.Net
{
    public interface IApiClient
    {
        Task<AuthTokens> LoginWithIdToken(string idToken);
        Task<AuthTokens> Refresh(string refreshToken);
        Task<UserMe>     GetMe(string accessToken = null);
        Task<UserMe>     SetNickname(string nickname);
        Task<LinkOut>    LinkIdentity(string provider, string sub, object claims=null);
        Task<UnlinkOut>  UnlinkIdentity(string provider);

        Task<GameSession> CreateSession(SessionStartReq req);
        Task Heartbeat(string sessionId, long ms);
        Task SubmitResult(SessionResultReq req);
    }
}
