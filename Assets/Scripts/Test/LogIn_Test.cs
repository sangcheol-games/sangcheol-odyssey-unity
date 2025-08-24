using UnityEngine;
using UnityEngine.Networking;

public class GoogleOAuthLogin : MonoBehaviour
{

    // Google Cloud Console에서 발급받은 OAuth Client ID
    [SerializeField] private string googleClientId = "730006761669-jl30bllk84qem5bgogd11ho6ua7a498e.apps.googleusercontent.com";

    // FastAPI 서버 콜백 URL
    [SerializeField] private string redirectUri = "http://localhost:5000/auth/callback";

    // UI 버튼에서 OnClick()에 등록할 메서드
    public void LoginWithGoogle()
    {
        string oauthUrl =
            "https://accounts.google.com/o/oauth2/v2/auth" +
            "?client_id=" + googleClientId +
            "&redirect_uri=" + UnityWebRequest.EscapeURL(redirectUri) +
            "&response_type=code" +
            "&scope=openid%20email%20profile" +
            "&access_type=offline" +        // refresh_token 발급(토큰이 만료도면 다시 받아온다)
            "&prompt=consent";              // 테스트시 편리하도록 매번 로그인할 때마다 계정을 선택하는 옵션

        Debug.Log("브라우저 열기: " + oauthUrl);
        Application.OpenURL(oauthUrl);
    }
}