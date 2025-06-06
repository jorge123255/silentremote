using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SilentRemote.Relay.WebSupport;

namespace SilentRemote.Relay
{
    /// <summary>
    /// Main relay server class that handles both WebSocket signaling and HTTP web support
    /// </summary>
    public class RelayServer
    {
        private string _host;
        private int _wsPort;
        private int _httpPort;
        private bool _useHttps;
        private WebSupportIntegration _webSupport;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        
        private event EventHandler<WebSessionInfo> WebSessionRegistered;
        private event EventHandler<string> ClientConnected;

        /// <summary>
        /// Creates a new relay server instance
        /// </summary>
        /// <param name="host">Host name, e.g. relay.nextcloudcyber.com</param>
        /// <param name="wsPort">WebSocket port for signaling</param>
        /// <param name="httpPort">HTTP/HTTPS port for web support</param>
        /// <param name="useHttps">Whether to use HTTPS for web support</param>
        public RelayServer(string host, int wsPort, int httpPort, bool useHttps)
        {
            _host = host;
            _wsPort = wsPort;
            _httpPort = httpPort;
            _useHttps = useHttps;
            _webSupport = new WebSupportIntegration();
            _cts = new CancellationTokenSource();
        }
        
        /// <summary>
        /// Starts the relay server
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
                return;
                
            try
            {
                // Initialize web support
                await _webSupport.InitializeAsync(_host, _httpPort, _useHttps);
                
                // In a full implementation, we would also initialize WebSocket signaling here
                // For now, we'll just simulate it
                
                _isRunning = true;
                Console.WriteLine($"Relay server started at {_host}");
                Console.WriteLine($"WebSocket endpoint: {GetWebSocketUrl()}");
                Console.WriteLine($"Web support endpoint: {GetWebUrl()}");
                
                // Start background monitoring
                _ = MonitorAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start relay server: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// Stops the relay server
        /// </summary>
        public async Task StopAsync()
        {
            if (!_isRunning)
                return;
                
            _cts.Cancel();
            
            // Stop web support
            _webSupport.Stop();
            
            // In a full implementation, we would also stop WebSocket signaling here
            
            _isRunning = false;
            Console.WriteLine("Relay server stopped");
        }
        
        /// <summary>
        /// Registers a new web support session
        /// </summary>
        /// <param name="sessionKey">Unique session key</param>
        /// <param name="sessionName">Human-readable session name</param>
        /// <param name="serverId">ID of the server that created the session</param>
        /// <param name="expiresInMinutes">How long the session is valid for (in minutes)</param>
        /// <param name="oneTimeSession">Whether this is a one-time session (deletes after use)</param>
        public void RegisterWebSession(string sessionKey, string sessionName, string serverId, int expiresInMinutes = 30, bool oneTimeSession = true)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Relay server is not running");
            
            // Get the WebSocket URL for the relay server
            string relayUrl = GetWebSocketUrl();
                
            // Register with web support integration
            _webSupport.RegisterWebSession(sessionKey, sessionName, serverId, relayUrl, expiresInMinutes, oneTimeSession);
            
            // Notify listeners
            WebSessionRegistered?.Invoke(this, new WebSessionInfo
            {
                SessionKey = sessionKey,
                SessionName = sessionName,
                ServerId = serverId,
                RelayUrl = relayUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
                OneTimeSession = oneTimeSession
            });
        }
        
        /// <summary>
        /// Gets the web URL for a support session
        /// </summary>
        public string GetWebSessionUrl(string sessionKey)
        {
            if (!_isRunning)
                throw new InvalidOperationException("Relay server is not running");
                
            return $"{GetWebUrl()}connect?session={sessionKey}";
        }
        
        /// <summary>
        /// Gets the WebSocket URL for the relay server
        /// </summary>
        public string GetWebSocketUrl()
        {
            string protocol = _useHttps ? "wss" : "ws";
            return $"{protocol}://{_host}:{_wsPort}";
        }
        
        /// <summary>
        /// Gets the web URL for the relay server
        /// </summary>
        public string GetWebUrl()
        {
            string protocol = _useHttps ? "https" : "http";
            return $"{protocol}://{_host}:{_httpPort}/";
        }
        
        /// <summary>
        /// Background monitoring task
        /// </summary>
        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // In a full implementation, we would monitor active connections, prune expired sessions, etc.
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in relay server monitoring: {ex.Message}");
            }
        }
    }
}
