using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace SilentRemote.Relay.WebSupport
{
    /// <summary>
    /// Integrates web support functionality with the relay server
    /// </summary>
    public class WebSupportIntegration
    {
        private WebSupportHandler _webSupportHandler;
        private string _contentRoot;
        private string _httpEndpoint;
        private bool _isInitialized;

        /// <summary>
        /// Initialize the web support integration
        /// </summary>
        /// <param name="relayServerHost">The relay server hostname (e.g. relay.nextcloudcyber.com)</param>
        /// <param name="relayServerPort">The relay server port for HTTP/HTTPS</param>
        /// <param name="useHttps">Whether to use HTTPS or HTTP</param>
        public async Task InitializeAsync(string relayServerHost, int relayServerPort, bool useHttps)
        {
            if (_isInitialized)
                return;

            // Set up content directory
            _contentRoot = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "WebSupport", "Content");

            // Create content directory if it doesn't exist
            if (!Directory.Exists(_contentRoot))
            {
                Directory.CreateDirectory(_contentRoot);
                await ExtractEmbeddedContentAsync();
            }

            // Determine the HTTP endpoint
            string protocol = useHttps ? "https" : "http";
            _httpEndpoint = $"{protocol}://{relayServerHost}:{relayServerPort}/";

            // Create the web support handler
            _webSupportHandler = new WebSupportHandler(_contentRoot, _httpEndpoint);
            
            // Start the handler
            _webSupportHandler.Start();
            _isInitialized = true;

            Console.WriteLine($"Web support integration initialized at {_httpEndpoint}");
        }

        /// <summary>
        /// Register a new web support session
        /// </summary>
        /// <param name="sessionKey">Unique session key</param>
        /// <param name="sessionName">Human-readable session name</param>
        /// <param name="serverId">ID of the server that created the session</param>
        /// <param name="relayUrl">WebSocket URL of the relay server</param>
        /// <param name="expiresInMinutes">How long the session is valid for (in minutes)</param>
        /// <param name="oneTimeSession">Whether this is a one-time session (deletes after use)</param>
        public void RegisterWebSession(
            string sessionKey, 
            string sessionName, 
            string serverId, 
            string relayUrl, 
            int expiresInMinutes = 30, 
            bool oneTimeSession = true)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Web support integration not initialized");
            }

            var sessionInfo = new WebSessionInfo
            {
                SessionKey = sessionKey,
                SessionName = sessionName,
                ServerId = serverId,
                RelayUrl = relayUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
                OneTimeSession = oneTimeSession
            };

            _webSupportHandler.RegisterWebSession(sessionInfo);
            
            Console.WriteLine($"Registered web session: {sessionKey} for server {serverId} with relay URL {relayUrl}");
        }

        /// <summary>
        /// Get the web URL for a session
        /// </summary>
        /// <param name="sessionKey">The session key</param>
        /// <returns>The full URL to access the web support page</returns>
        public string GetWebUrlForSession(string sessionKey)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Web support integration not initialized");
            }

            return $"{_httpEndpoint}connect?session={sessionKey}";
        }

        /// <summary>
        /// Stop the web support integration
        /// </summary>
        public void Stop()
        {
            if (_isInitialized && _webSupportHandler != null)
            {
                _webSupportHandler.Stop();
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Extract embedded content files if needed
        /// </summary>
        private async Task ExtractEmbeddedContentAsync()
        {
            // In production, we would extract embedded content files here if they don't exist on disk
            // For now, we'll create placeholder files if they don't exist

            string[] requiredFiles = new[] { "index.html", "landing.html", "invalid_session.html", "expired_session.html" };

            foreach (var file in requiredFiles)
            {
                string filePath = Path.Combine(_contentRoot, file);
                if (!File.Exists(filePath))
                {
                    // Create a very basic placeholder file
                    await File.WriteAllTextAsync(filePath, $"<!DOCTYPE html><html><body><h1>{file}</h1><p>This is a placeholder file.</p></body></html>");
                }
            }
        }
    }
}
