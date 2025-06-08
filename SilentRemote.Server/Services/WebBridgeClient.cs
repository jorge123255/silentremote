using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SilentRemote.Common.Models;

namespace SilentRemote.Server.Services
{
    /// <summary>
    /// Client for communicating with the Quasar web bridge service
    /// </summary>
    public class WebBridgeClient
    {
        private readonly string _webBridgeUrl;
        private readonly HttpClient _httpClient;

        public WebBridgeClient(string webBridgeUrl)
        {
            _webBridgeUrl = webBridgeUrl.TrimEnd('/');
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Create a new web session via the web bridge
        /// </summary>
        /// <param name="serverId">Server ID to associate with the session</param>
        /// <param name="sessionName">Optional friendly name for the session</param>
        /// <param name="expiresInMinutes">Session expiration time in minutes (default: 30)</param>
        /// <param name="oneTimeSession">Whether session is valid for one-time use only (default: true)</param>
        /// <returns>Web session information including session key and URLs</returns>
        public async Task<WebSessionInfo> CreateSessionAsync(
            string serverId,
            string sessionName = null,
            int expiresInMinutes = 30,
            bool oneTimeSession = true)
        {
            try
            {
                var requestData = new
                {
                    serverId,
                    sessionName,
                    expiresInMinutes,
                    oneTimeSession
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(requestData),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync($"{_webBridgeUrl}/session/create", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var bridgeResponse = JsonConvert.DeserializeObject<WebBridgeResponse>(responseContent);

                if (bridgeResponse == null)
                {
                    throw new Exception("Web bridge returned an invalid response");
                }

                Console.WriteLine($"Web bridge response: SessionKey={bridgeResponse.SessionKey}, RelayServer={bridgeResponse.RelayServer}");
                
                // Convert bridge response to WebSessionInfo
                return new WebSessionInfo
                {
                    SessionKey = bridgeResponse.SessionKey ?? "unknown",
                    SessionName = bridgeResponse.SessionName ?? sessionName,
                    ServerId = bridgeResponse.ServerId ?? serverId,
                    RelayUrl = bridgeResponse.RelayServer ?? "unknown",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = bridgeResponse.ExpiresAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(bridgeResponse.ExpiresAt) : DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
                    OneTimeSession = oneTimeSession
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create web session via web bridge: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validate a session key with the web bridge
        /// </summary>
        /// <param name="sessionKey">Session key to validate</param>
        /// <returns>True if session is valid</returns>
        public async Task<bool> ValidateSessionAsync(string sessionKey)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_webBridgeUrl}/session/validate/{sessionKey}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the full web client URL for a specific session
        /// </summary>
        /// <param name="sessionKey">Session key</param>
        /// <returns>Full URL to the web client with session information</returns>
        public string GetSessionUrl(string sessionKey)
        {
            return $"{_webBridgeUrl}/client?session={sessionKey}";
        }

        /// <summary>
        /// Check if the web bridge is available
        /// </summary>
        /// <returns>True if the bridge is responding</returns>
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_webBridgeUrl}/health");
                return response.IsSuccessStatusCode && 
                       (await response.Content.ReadAsStringAsync()).Contains("healthy");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Response object from the web bridge session creation endpoint
        /// </summary>
        private class WebBridgeResponse
        {
            public string SessionKey { get; set; }
            public string SessionName { get; set; }
            public string ServerId { get; set; }
            public long ExpiresAt { get; set; }
            public string SessionUrl { get; set; }
            public string RelayServer { get; set; }
            public string RelayServerName { get; set; }
        }
    }
}
