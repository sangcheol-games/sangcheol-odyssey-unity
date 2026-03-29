using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SteamLoginController : MonoBehaviour
{
    [Header("UI")]
    public Button   LoginButton;
    public TMP_Text StatusText;
    public TMP_Text ResultText;

    private SteamAuthService _authService;

    private void Awake()
    {
        _authService = new SteamAuthService();
    }

    private void Start()
    {
        SetStatus("Steam Manager: " + (SteamManager.Initialized ? "OK" : "NOT INITIALIZED"));

        if (LoginButton)
            LoginButton.onClick.AddListener(() => _ = RunLogin());
    }

    private async Task RunLogin()
    {
        LoginButton.interactable = false;
        SetStatus("로그인 중...");
        SetResult("");

        try
        {
            var (playerId, accessToken) = await _authService.SignInWithSteamAsync();

            SetStatus("로그인 성공");
            SetResult($"Player ID : {playerId}\nToken      : {Truncate(accessToken, 24)}...");
        }
        catch (Exception e)
        {
            SetStatus($"오류: {e.Message}");
            Debug.LogException(e);
        }
        finally
        {
            LoginButton.interactable = true;
        }
    }

    private void SetStatus(string msg)
    {
        Debug.Log($"[SteamLogin] {msg}");
        if (StatusText) StatusText.text = msg;
    }

    private void SetResult(string msg)
    {
        if (ResultText) ResultText.text = msg;
    }

    private static string Truncate(string s, int len) =>
        string.IsNullOrEmpty(s) ? "(null)" : s.Length <= len ? s : s[..len];

    private void OnDestroy()
    {
        _authService?.Dispose();
    }
}
