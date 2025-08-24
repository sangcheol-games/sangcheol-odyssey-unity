using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Net;
using System.Web;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class GoogleIdTokenLogin : MonoBehaviour
{
    [Header("Google Web Client")]
    [SerializeField]
    private string googleWebClientId =
        "198322966158-nq5f8lrkknbtpn5rdqh4vlqviph5tejp.apps.googleusercontent.com";

    [Header("Backend")]
    [SerializeField] private string backendBaseUrl = "http://192.168.115.189:8000";
    private string VerifyUrl => backendBaseUrl.TrimEnd('/') + "/v1/auth/google/verify-id-token";

    public Action<string> OnLoginSuccess;
    public Action<string> OnLoginError;

    private HttpListener listener;
    private CancellationTokenSource cts;
    private const int PORT = 51789;

    public void LoginWithGoogle()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            StartLocalServer();
            Application.OpenURL($"http://127.0.0.1:{PORT}/");
            string idToken = await WaitForCredentialAsync(cts.Token, 180_000);
            if (string.IsNullOrEmpty(idToken))
                throw new Exception("No ID token received from browser.");
            await VerifyIdTokenAtBackend(idToken);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            OnLoginError?.Invoke(ex.Message);
        }
        finally
        {
            StopLocalServer();
        }
    }

    private void StartLocalServer()
    {
        StopLocalServer();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{PORT}/");
        listener.Start();
        cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
    }

    private void StopLocalServer()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
        listener = null;
        cts = null;
    }

    private async Task AcceptLoopAsync(HttpListener ln, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx = null;
            try
            {
                ctx = await ln.GetContextAsync();
                string path = ctx.Request.Url.AbsolutePath;

                if (path == "/")
                {
                    string html = BuildSigninHtml(googleWebClientId);
                    await WriteHtmlAsync(ctx, 200, html);
                }
                else if (path == "/deliver")
                {
                    string body;
                    using (var sr = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                        body = await sr.ReadToEndAsync();

                    string credential = null;
                    if (!string.IsNullOrEmpty(ctx.Request.QueryString["credential"]))
                        credential = ctx.Request.QueryString["credential"];
                    else if (body?.Contains("credential=") == true)
                        credential = HttpUtility.UrlDecode(
                            body.Split(new[] { "credential=" }, StringSplitOptions.None)[1]
                        );

                    if (!string.IsNullOrEmpty(credential))
                    {
                        lastCredential = credential;
                        await WritePlainAsync(ctx, 200, "OK");
                    }
                    else
                    {
                        await WritePlainAsync(ctx, 400, "Missing credential");
                    }
                }
                else
                {
                    await WritePlainAsync(ctx, 404, "Not Found");
                }
            }
            catch { }
        }
    }

    private string lastCredential = null;
    private async Task<string> WaitForCredentialAsync(CancellationToken token, int timeoutMs)
    {
        int waited = 0;
        while (string.IsNullOrEmpty(lastCredential) && !token.IsCancellationRequested && waited < timeoutMs)
        {
            await Task.Delay(100, token);
            waited += 100;
        }
        return lastCredential;
    }

    private static async Task WriteHtmlAsync(HttpListenerContext ctx, int status, string html)
    {
        byte[] buf = Encoding.UTF8.GetBytes(html);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
        ctx.Response.Close();
    }

    private static async Task WritePlainAsync(HttpListenerContext ctx, int status, string text)
    {
        byte[] buf = Encoding.UTF8.GetBytes(text ?? "");
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
        ctx.Response.Close();
    }

    private string BuildSigninHtml(string clientId)
    {
        return $@"<!doctype html>
<html lang='ko'>
<head>
  <meta charset='utf-8'/>
  <meta name='viewport' content='width=device-width,initial-scale=1'/>
  <title>Sign in with Google</title>
  <script src='https://accounts.google.com/gsi/client' async defer></script>
  <script>
    function handleCredentialResponse(resp) {{
      const cred = resp && resp.credential;
      if (!cred) {{
        document.body.innerHTML = '<h3>로그인 실패: credential 없음</h3>';
        return;
      }}
      fetch('/deliver?credential=' + encodeURIComponent(cred), {{ method:'POST' }})
        .then(() => {{ document.body.innerHTML = '<h2>로그인 완료</h2><p>게임으로 돌아가세요.</p>'; }})
        .catch(e => {{ document.body.innerHTML = '<h3>전송 실패</h3><pre>' + e + '</pre>'; }});
    }}
    window.onload = () => {{
      google.accounts.id.initialize({{ client_id: '{clientId}', callback: handleCredentialResponse }});
      google.accounts.id.prompt(); 
      google.accounts.id.renderButton(document.getElementById('btn'), {{
        type: 'standard', theme: 'outline', size: 'large'
      }});
    }};
  </script>
  <style>
    body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial; padding: 32px; }}
  </style>
</head>
<body>
  <h2>Google 로그인</h2>
  <div id='btn'></div>
</body>
</html>";
    }

    private async Task VerifyIdTokenAtBackend(string idToken)
    {
        var payload = JsonUtility.ToJson(new IdTokenRequest { id_token = idToken });

        using var req = new UnityWebRequest(VerifyUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception($"HTTP {req.responseCode}: {req.error}\n{req.downloadHandler.text}");

        var resp = JsonUtility.FromJson<TokenResponseLike>(req.downloadHandler.text);
        if (string.IsNullOrEmpty(resp.access_token))
            throw new Exception("No access_token in response: " + req.downloadHandler.text);

        OnLoginSuccess?.Invoke(resp.access_token);
    }

    [Serializable] private class IdTokenRequest { public string id_token; }
    [Serializable]
    private class TokenResponseLike
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public string token_type;
        public bool is_new_user;
    }

    private void OnDestroy() => StopLocalServer();
}
