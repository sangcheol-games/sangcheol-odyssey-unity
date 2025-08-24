using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class FastApiAllInOneTester : MonoBehaviour
{
    // === 서버/구글 설정 입력 ===
    public string BaseUrl = "http://127.0.0.1:8000";
    public string GoogleClientId = ""; // GCP 콘솔의 Web client ID. 서버 .env와 동일해야 함
    public string ServerRedirectUri = "http://127.0.0.1:8000/v1/auth/google/callback"; // 서버가 콜백 받는 주소

    // === 런타임 상태 ===
    string codeVerifier;
    string codeChallenge;
    string sessionId;
    string accessToken;
    string refreshToken;
    string nicknameInput;
    string providerSub = ""; // link/unlink 테스트용
    Vector2 scroll;
    string logText = "";

    void Start()
    {
        GeneratePkce();
        nicknameInput = "Player_" + UnityEngine.Random.Range(1000, 9999); // 여기서 초기화
        Log("Ready. Set BaseUrl/ClientId/RedirectUri if needed, then [Init+Open Google Login].");
    }

    void GeneratePkce()
    {
        // RFC 7636 S256
        byte[] random = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
        codeVerifier = Base64UrlEncode(random) + Base64UrlEncode(random); // 좀 길게
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        codeChallenge = Base64UrlEncode(hash);
    }

    static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 700, Screen.height - 20));
        GUILayout.Label("<b>FastAPI All-in-one Tester (OnGUI)</b>");
        GUILayout.Label("BaseUrl"); BaseUrl = GUILayout.TextField(BaseUrl);
        GUILayout.Label("Google Web Client ID (must match server .env GOOGLE_CLIENT_ID_WEB / GOOGLE_CLIENT_IDS)");
        GoogleClientId = GUILayout.TextField(GoogleClientId);
        GUILayout.Label("Server Redirect URI (in Google console & .env)");
        ServerRedirectUri = GUILayout.TextField(ServerRedirectUri);

        GUILayout.Space(8);
        if (GUILayout.Button("1) Init Session + Open Google Login (Server Callback)"))
        {
            StartCoroutine(CoInitSessionAndOpen());
        }

        if (GUILayout.Button("2) Poll Session (Get Tokens)"))
        {
            if (string.IsNullOrEmpty(sessionId)) Log("No sessionId yet.");
            else StartCoroutine(CoPoll());
        }

        GUILayout.Space(8);
        GUILayout.Label("Access Token (read-only)");
        GUI.enabled = false; GUILayout.TextField(accessToken ?? ""); GUI.enabled = true;
        GUILayout.Label("Refresh Token (read-only)");
        GUI.enabled = false; GUILayout.TextField(refreshToken ?? ""); GUI.enabled = true;

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ping")) StartCoroutine(CoPing());
        if (GUILayout.Button("Me")) StartCoroutine(CoMe());
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Nickname"); nicknameInput = GUILayout.TextField(nicknameInput);
        if (GUILayout.Button("Set Nickname (once)")) StartCoroutine(CoSetNickname(nicknameInput));

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Access Token")) StartCoroutine(CoRefresh());
        if (GUILayout.Button("Logout (invalidate refresh)")) StartCoroutine(CoLogout());
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("Identity (optional) - provider_sub for google");
        providerSub = GUILayout.TextField(providerSub);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Link Identity (google)")) StartCoroutine(CoLinkIdentity("google", providerSub));
        if (GUILayout.Button("Unlink Identity (google)")) StartCoroutine(CoUnlinkIdentity("google"));
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("<b>Logs</b>");
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300));
        GUILayout.TextArea(logText, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    IEnumerator CoInitSessionAndOpen()
    {
        // 서버에 code_verifier 저장
        var body = JsonUtility.ToJson(new InitBody { code_verifier = codeVerifier });
        var req = UnityWebRequest.PostWwwForm($"{BaseUrl}/v1/auth/session/init", ""); // dummy
        req.method = "POST";
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Log($"Init failed: {req.responseCode} {req.error} {req.downloadHandler.text}");
            yield break;
        }

        var resp = JsonUtility.FromJson<AuthUrlResponse>(req.downloadHandler.text);
        sessionId = resp.session_id;
        Log($"Session initialized. sid={sessionId}");

        // 구글 권한 URL(서버 콜백을 사용) 직접 구성
        // 서버 콜백은 /v1/auth/google/callback 에서 sid(state)와 code로 토큰 교환 & 검증 & 세션 완료 처리함.
        // 서버쪽은 settings.GOOGLE_CLIENT_ID_WEB / GOOGLE_REDIRECT_URI 값을 사용함. 
        string authUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={UnityWebRequest.EscapeURL(GoogleClientId)}" +
            $"&redirect_uri={UnityWebRequest.EscapeURL(ServerRedirectUri)}" +
            "&response_type=code" +
            "&scope=" + UnityWebRequest.EscapeURL("openid email profile") +
            "&code_challenge_method=S256" +
            $"&code_challenge={codeChallenge}" +
            $"&state={sessionId}";

        Application.OpenURL(authUrl);
        Log("Opened browser for Google login.");
    }

    IEnumerator CoPoll()
    {
        var url = $"{BaseUrl}/v1/auth/session/poll?sid={UnityWebRequest.EscapeURL(sessionId)}";
        var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();
        var txt = req.downloadHandler.text;
        if (req.responseCode == 202) { Log("Poll: pending"); yield break; }
        if (req.responseCode == 200)
        {
            var tok = JsonUtility.FromJson<TokenResponse>(txt);
            accessToken = tok.access_token;
            refreshToken = tok.refresh_token;
            Log($"Poll OK. is_new_user={tok.is_new_user}, expires_in={tok.expires_in}");
        }
        else Log($"Poll error {req.responseCode}: {txt}");
    }

    IEnumerator CoPing()
    {
        var req = UnityWebRequest.Get($"{BaseUrl}/v1/ping/");
        yield return req.SendWebRequest();
        Log($"Ping {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoMe()
    {
        var req = UnityWebRequest.Get($"{BaseUrl}/v1/users/me");
        AttachBearer(req);
        yield return req.SendWebRequest();
        Log($"Me {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoSetNickname(string nickname)
    {
        var body = JsonUtility.ToJson(new NicknameBody { nickname = nickname });
        var req = UnityWebRequest.PostWwwForm($"{BaseUrl}/v1/users/me/nickname", "");
        req.method = "PATCH";
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        AttachBearer(req);
        yield return req.SendWebRequest();
        Log($"SetNickname {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoRefresh()
    {
        var body = JsonUtility.ToJson(new RefreshBody { refresh_token = refreshToken });
        var req = UnityWebRequest.PostWwwForm($"{BaseUrl}/v1/auth/refresh", "");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        if (req.responseCode == 200)
        {
            var tok = JsonUtility.FromJson<TokenResponse>(req.downloadHandler.text);
            accessToken = tok.access_token;
            refreshToken = tok.refresh_token;
        }
        Log($"Refresh {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoLogout()
    {
        var req = UnityWebRequest.PostWwwForm($"{BaseUrl}/v1/auth/logout", "");
        req.downloadHandler = new DownloadHandlerBuffer();
        AttachBearer(req);
        yield return req.SendWebRequest();
        Log($"Logout {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoLinkIdentity(string provider, string providerSub)
    {
        if (string.IsNullOrEmpty(providerSub)) { Log("provider_sub empty."); yield break; }
        var body = JsonUtility.ToJson(new LinkBody { provider_sub = providerSub, claims = null });
        var req = UnityWebRequest.PostWwwForm($"{BaseUrl}/v1/identities/{provider}", "");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        AttachBearer(req);
        yield return req.SendWebRequest();
        Log($"LinkIdentity {req.responseCode}: {req.downloadHandler.text}");
    }

    IEnumerator CoUnlinkIdentity(string provider)
    {
        var req = UnityWebRequest.Delete($"{BaseUrl}/v1/identities/{provider}");
        AttachBearer(req);
        yield return req.SendWebRequest();
        Log($"UnlinkIdentity {req.responseCode}: {req.downloadHandler.text}");
    }

    void AttachBearer(UnityWebRequest req)
    {
        if (!string.IsNullOrEmpty(accessToken))
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
    }

    void Log(string s) { logText += $"[{DateTime.Now:HH:mm:ss}] {s}\n"; }

    // === DTOs (Unity JsonUtility-friendly) ===
    [Serializable] class InitBody { public string code_verifier; }
    [Serializable] class AuthUrlResponse { public string auth_url; public string session_id; }
    [Serializable] class TokenResponse { public string access_token; public string refresh_token; public int expires_in; public bool is_new_user; public string token_type; }
    [Serializable] class NicknameBody { public string nickname; }
    [Serializable] class RefreshBody { public string refresh_token; }
    [Serializable] class LinkBody { public string provider_sub; public object claims; }
}
