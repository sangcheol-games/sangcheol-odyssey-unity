using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using SCOdyssey.Net;
using SCOdyssey.Testing.Items;

namespace SCOdyssey.Testing.UI
{
    public sealed class SmokeRunnerPanel : MonoBehaviour
    {
        [SerializeField] private TestingConfig config;
        [SerializeField] private Button runSmoke;
        [SerializeField] private Text status;

        public Func<string, IApiClient> HttpFactory;

        private IApiClient _client;

        private void Start()
        {
            IApiClient baseClient = config.useMockApi
                ? new MockApiClient()
                : (HttpFactory != null ? HttpFactory(config.httpBaseUrl) : CreateHttpClient(config.httpBaseUrl));
            _client = new ApiClientWithAdversity(baseClient, config);
        }

        private static IApiClient CreateHttpClient(string baseUrl)
        {
            var t = Type.GetType("SCOdyssey.Net.HttpApiClient, SCOdyssey.Net");
            if (t == null) throw new InvalidOperationException("HttpApiClient type not found. Inject HttpFactory.");
            return (IApiClient)Activator.CreateInstance(t, new object[] { baseUrl });
        }

        private void OnEnable()
        {
            runSmoke.onClick.AddListener(() => _ = RunSmokeAsync());
        }

        private async Task RunSmokeAsync()
        {
            try
            {
                status.text = "SessionInit";
                var init = await _client.SessionInit("dev-code-verifier");

                status.text = "SessionPoll";
                var tok = await _client.SessionPoll(init.session_id);

                status.text = "Ping";
                await _client.Ping();

                status.text = "GetMe";
                await _client.GetMe(tok.access_token);

                status.text = "SetNickname";
                var key = $"nick:{Guid.NewGuid():N}";
                var item = new NicknameUpdateItem(_client, "tester_" + UnityEngine.Random.Range(0, 9999), key);
                if (config.enableOfflineQueue)
                {
                    var q = FindAnyObjectByType<OfflineQueue>();
                    if (q != null) q.Enqueue(item);
                    else await item.TrySendAsync();
                }
                else
                {
                    await item.TrySendAsync();
                }

                status.text = "OK";
            }
            catch (ArgumentException)
            {
                status.text = "Poll: google_error";
            }
            catch (InvalidOperationException ex)
            {
                status.text = ex.Message.Contains("404") ? "Poll: not found" : "Invalid";
            }
            catch (ApplicationException)
            {
                status.text = "Poll: pending";
            }
            catch (Exception ex)
            {
                status.text = "FAIL: " + ex.Message;
            }
        }
    }
}
