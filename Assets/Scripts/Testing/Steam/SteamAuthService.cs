using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using Steamworks;

public class SteamAuthService : IDisposable
{
    // 티켓 발급 대상 식별자.
    // GetAuthTicketForWebApi에 넘기면 Steam이 "이 identity 전용" 티켓으로 서명.
    readonly string identity = "unityauthenticationservice";

    // GetTicketForWebApiResponse_t 콜백 수신용.
    // 반환값을 멤버 변수에 보관하지 않으면 GC가 수거해 콜백이 호출되지 않음.
    Callback<GetTicketForWebApiResponse_t> m_AuthTicketForWebApiResponseCallback;

    // 티켓 콜백을 async/await로 연결하는 브릿지
    private TaskCompletionSource<string> m_TicketTcs;

    // 현재 발급 요청 중인 티켓 핸들 (콜백에서 내 요청인지 식별용)
    private HAuthTicket m_PendingTicket = HAuthTicket.Invalid;

    public bool IsDisposed { get; private set; }

    public SteamAuthService()
    {
        // 생성 시점에 콜백 등록. 이후 SteamAPI.RunCallbacks() 호출마다 이벤트 수신.
        m_AuthTicketForWebApiResponseCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnAuthCallback);
    }


    /// Steam 세션 티켓을 발급받아 hex string으로 반환
    /// 반드시 Scene에 SteamManager가 존재해야 SteamAPI.RunCallbacks() 호출가능
    public Task<string> GetAuthTicketHexAsync()
    {
        if (!SteamManager.Initialized)
            throw new InvalidOperationException("SteamManager가 초기화되지 않았습니다.");
        if (m_TicketTcs != null && !m_TicketTcs.Task.IsCompleted)
            throw new InvalidOperationException("이미 티켓 발급 중입니다.");

        m_TicketTcs = new TaskCompletionSource<string>();

        // 타임아웃 설정
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() =>
            m_TicketTcs.TrySetException(new TimeoutException("Steam 티켓 콜백 타임아웃 (10초)")));

        // 티켓 발급 요청. 실제 티켓 데이터는 OnAuthCallback이 호출될 때 m_rgubTicket에 반환
        m_PendingTicket = SteamUser.GetAuthTicketForWebApi(identity);

        if (m_PendingTicket == HAuthTicket.Invalid)
            m_TicketTcs.TrySetException(new Exception("GetAuthTicketForWebApi 실패: Invalid 핸들 반환"));

        return m_TicketTcs.Task;
    }

    // GetAuthTicketForWebApi 요청에 대한 Steam 콜백.
    // SteamManager.Update()에서 SteamAPI.RunCallbacks()가 호출될 때 실행
    void OnAuthCallback(GetTicketForWebApiResponse_t callback)
    {
        // 다른 요청의 콜백이 섞여 들어오는 경우 무시
        if (callback.m_hAuthTicket != m_PendingTicket)
            return;
        if (callback.m_eResult != EResult.k_EResultOK)
        {
            m_TicketTcs?.TrySetException(new Exception($"Steam 티켓 발급 실패: {callback.m_eResult}"));
            return;
        }

        // m_cubTicket 만큼만 변환 (버퍼 전체가 아닌 유효 바이트만 사용)
        string m_SessionTicket = BitConverter.ToString(callback.m_rgubTicket, 0, callback.m_cubTicket)
                                             .Replace("-", string.Empty);
        Debug.Log("Steam Login success. Session Ticket: " + m_SessionTicket);

        // 티켓을 받은 즉시 Dispose하여 콜백 해제
        m_AuthTicketForWebApiResponseCallback?.Dispose();
        m_AuthTicketForWebApiResponseCallback = null;

        // 대기 중인 Task를 완료시켜 GetAuthTicketHexAsync의 await를 깨움
        m_TicketTcs?.TrySetResult(m_SessionTicket);
        m_PendingTicket = HAuthTicket.Invalid;
    }

    /// <summary>
    /// Steam 티켓을 발급받아 Unity Authentication으로 로그인합니다.
    /// </summary>
    /// <returns>(playerId, accessToken)</returns>
    public async Task<(string playerId, string accessToken)> SignInWithSteamAsync()
    {
        // Unity Gaming Services 초기화 (이미 초기화된 경우 무시)
        await UnityServices.InitializeAsync();

        // Steam 티켓 발급 대기 (OnAuthCallback 콜백이 올 때까지 비동기 대기)
        string steamTicket = await GetAuthTicketHexAsync();

        // Steam Web API로 검증 후 Player ID 발급
        await AuthenticationService.Instance.SignInWithSteamAsync(steamTicket, identity);

        return (AuthenticationService.Instance.PlayerId,
                AuthenticationService.Instance.AccessToken);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        // OnAuthCallback 이전에 Dispose되는 경우 콜백 해제
        m_AuthTicketForWebApiResponseCallback?.Dispose();
        m_AuthTicketForWebApiResponseCallback = null;
    }
}
