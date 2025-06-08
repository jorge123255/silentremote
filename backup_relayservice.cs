using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SilentRemote.Common.Models;

namespace SilentRemote.Server.Services
{
    /// <summary>
    /// Represents a web support session for client downloads
    /// </summary>
    public class WebSessionInfo
    {
        public string SessionKey { get; set; }
        public string SessionName { get; set; }
        public string ServerId { get; set; }
        public string RelayUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool OneTimeSession { get; set; } = true;
    }
    
    /// <summary>
    /// Service responsible for communication with the relay server for signaling and web session management
    /// </summary>
    public class RelaySignalingService
    {
        private readonly string _relayUrl;
        private readonly string _serverId;
        private readonly string _authToken;
        private readonly ILogger<RelaySignalingService> _logger;
        private readonly WebBridgeClient _webBridge;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected;
        
        // Collection of active web sessions
        private Dictionary<string, WebSessionInfo> _activeSessions = new Dictionary<string, WebSessionInfo>();

        public RelaySignalingService(string relayUrl, string serverId, string authToken, string webBridgeUrl = null, ILogger<RelaySignalingService> logger = null)
        {
            _relayUrl = relayUrl;
            _serverId = serverId;
            _authToken = authToken;
            _logger = logger;
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            
            // Initialize web bridge client if URL is provided
            if (!string.IsNullOrEmpty(webBridgeUrl))
            {
                _webBridge = new WebBridgeClient(webBridgeUrl);
                _logger?.LogInformation("Web bridge client initialized with URL: {BridgeUrl}", webBridgeUrl);
            }
        }

        /// <summary>
        /// Connects to the relay server for signaling
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isConnected)
                return;

            try
            {
                Uri serverUri = new Uri($"{_relayUrl}/server");
                await _webSocket.ConnectAsync(serverUri, _cts.Token);
                
                // Send authentication message
                var authMessage = new
                {
                    type = "auth",
                    serverId = _serverId,
                    token = _authToken
                };
                
                await SendMessageAsync(authMessage);
                
                // Start listening for messages
                _ = ReceiveMessagesAsync();
                
                _isConnected = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to relay server: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tests the connection to the relay server
        /// </summary>
        public async Task TestConnectionAsync()
        {
            if (!_isConnected)
                await ConnectAsync();
                
            // Send ping message
            var pingMessage = new
            {
                type = "ping",
                serverId = _serverId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            await SendMessageAsync(pingMessage);
            
            // In a real implementation, we would wait for a pong response
            // For simplicity, we're assuming the send succeeds
        }

        /// <summary>
        /// Disconnects from the relay server
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!_isConnected)
                return;
                
            _cts.Cancel();
            
            if (_webSocket.State == WebSocketState.Open)
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect requested", CancellationToken.None);
                
            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _isConnected = false;
        }

        /// <summary>
        /// Registers a new web session with the relay server
        /// </summary>
        /// <param name="sessionKey">Unique session key</param>
        /// <param name="sessionName">Name of the session</param>
        /// <param name="expirationMinutes">Expiration time in minutes (-1 for no expiration)</param>
        /// <param name="oneTimeSession">Whether this is a one-time session (deletes after use)</param>
        public async Task RegisterWebSessionAsync(string sessionKey, string sessionName, int expirationMinutes, bool oneTimeSession = true)
        {
            if (!_isConnected)
                await ConnectAsync();
            
            // Calculate expiration time
            DateTimeOffset? expiresAt = null;
            if (expirationMinutes > 0)
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);
            
            // Create web session info
            var sessionInfo = new WebSessionInfo
            {
                SessionKey = sessionKey,
                SessionName = sessionName,
                ServerId = _serverId,
                RelayUrl = _relayUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                OneTimeSession = oneTimeSession
            };
            
            // Add to local collection
            _activeSessions[sessionKey] = sessionInfo;
            
            // Send registration message to the relay server
            var registerMessage = new
            {
                type = "register_web_session",
                serverId = _serverId,
                sessionKey = sessionKey,
                sessionName = sessionName,
                relayUrl = _relayUrl,
                expiresAt = expiresAt?.ToUnixTimeMilliseconds(),
                oneTimeSession = oneTimeSession
            };
            
            await SendMessageAsync(registerMessage);
        }

        /// <summary>
        /// Gets the URL for a web session
        /// </summary>
        /// <param name="sessionKey">The session key</param>
        /// <returns>URL that clients can use to download and run the pre-configured client</returns>
        public string GetWebSessionUrl(string sessionKey)
        {
            // If web bridge is available, use that URL
            if (_webBridge != null)
            {
                return _webBridge.GetSessionUrl(sessionKey);
            }
            
            // Otherwise, fall back to direct relay URL
            return $"{GetWebRelayUrl()}/connect?session={sessionKey}";
        }
        
        /// <summary>
        /// Gets the web URL version of the relay URL (converts wss to https)
        /// </summary>
        private string GetWebRelayUrl()
        {
            // Convert WebSocket URL to HTTP URL
            if (_relayUrl.StartsWith("wss://"))
                return "https://" + _relayUrl.Substring(6);
            if (_relayUrl.StartsWith("ws://"))
                return "http://" + _relayUrl.Substring(5);
            
            return _relayUrl;
        }

        /// <summary>
        /// Sends a message to the relay server
        /// </summary>
        private async Task SendMessageAsync(object message)
        {
            string json = JsonConvert.SerializeObject(message);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
        }

        /// <summary>
        /// Background task to receive messages from the relay server
        /// </summary>
        private async Task ReceiveMessagesAsync()
        {
            try
            {
                byte[] buffer = new byte[4096];
                
                while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        _isConnected = false;
                        break;
                    }
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessMessage(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WebSocket receive: {ex.Message}");
                _isConnected = false;
            }
        }

        /// <summary>
        /// Processes incoming messages from the relay server
        /// </summary>
        private void ProcessMessage(string json)
        {
            try
            {
                dynamic message = JsonConvert.DeserializeObject(json);
                string messageType = message?.type?.ToString();
                
                switch (messageType)
                {
                    case "web_session_request":
                        // A user has visited the web support URL and is requesting a client download
                        HandleWebSessionRequest(message);
                        break;
                        
                    case "client_connected":
                        // A client has connected through the web session
                        HandleClientConnected(message);
                        break;
                        
                    case "pong":
                        // Response to our ping message
                        break;
                        
                    default:
                        Console.WriteLine($"Unknown message type: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a web session download request
        /// </summary>
        private async void HandleWebSessionRequest(dynamic message)
        {
            string sessionKey = message.sessionKey?.ToString();
            string clientInfo = message.clientInfo?.ToString();
            string platform = message.platform?.ToString();
            
            if (string.IsNullOrEmpty(sessionKey) || !_activeSessions.ContainsKey(sessionKey))
            {
                Console.WriteLine($"Invalid or unknown session key: {sessionKey}");
                return;
            }
            
            try
            {
                // In a real implementation, we would build the client specifically for this session
                // and provide the download URL or base64-encoded client package
                
                // For now, we'll just send a message back to the relay indicating success
                var responseMessage = new
                {
                    type = "web_session_response",
                    serverId = _serverId,
                    sessionKey = sessionKey,
                    status = "success",
                    clientId = Guid.NewGuid().ToString(),
                    downloadUrl = $"{GetWebRelayUrl()}/download/{sessionKey}" // This URL would be served by the relay server
                };
                
                await SendMessageAsync(responseMessage);
                
                Console.WriteLine($"Web session request processed for session {sessionKey}, platform: {platform}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling web session request: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handles notification that a client has connected through a web session
        /// </summary>
        private void HandleClientConnected(dynamic message)
        {
            string sessionKey = message.sessionKey?.ToString();
            string clientId = message.clientId?.ToString();
            
            if (string.IsNullOrEmpty(sessionKey) || !_activeSessions.ContainsKey(sessionKey))
            {
                Console.WriteLine($"Client connected with invalid or unknown session key: {sessionKey}");
                return;
            }
            
            Console.WriteLine($"Client {clientId} connected through web session {sessionKey}");
            
            // In a real implementation, this would notify the UI or trigger events
        }
    }

    /// <summary>
    /// Represents a web support session for client downloads
    /// </summary>
    public class WebSessionInfo
    {
        public string SessionKey { get; set; }
        public string SessionName { get; set; }
        public string ServerId { get; set; }
        public string RelayUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool OneTimeSession { get; set; } = true;
    }
    
    /// <summary>
    /// Creates a web session via the web bridge
    /// </summary>
    public class RelaySignalingService
    {
        private readonly string _relayUrl;
        private readonly string _serverId;
        private readonly string _authToken;
        private readonly ILogger<RelaySignalingService> _logger;
        private readonly WebBridgeClient _webBridge;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected;
        
        // Collection of active web sessions
        private Dictionary<string, WebSessionInfo> _activeSessions = new Dictionary<string, WebSessionInfo>();

        public RelaySignalingService(string relayUrl, string serverId, string authToken, string webBridgeUrl = null, ILogger<RelaySignalingService> logger = null)
        {
            _relayUrl = relayUrl;
            _serverId = serverId;
            _authToken = authToken;
            _logger = logger;
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            
            // Initialize web bridge client if URL is provided
            if (!string.IsNullOrEmpty(webBridgeUrl))
            {
                _webBridge = new WebBridgeClient(webBridgeUrl);
                _logger?.LogInformation("Web bridge client initialized with URL: {BridgeUrl}", webBridgeUrl);
            }
        }

        /// <summary>
        /// Connects to the relay server for signaling
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isConnected)
                return;

            try
            {
                Uri serverUri = new Uri($"{_relayUrl}/server");
                await _webSocket.ConnectAsync(serverUri, _cts.Token);
                
                // Send authentication message
                var authMessage = new
                {
                    type = "auth",
                    serverId = _serverId,
                    token = _authToken
                };
                
                await SendMessageAsync(authMessage);
                
                // Start listening for messages
                _ = ReceiveMessagesAsync();
                
                _isConnected = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to relay server: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tests the connection to the relay server
        /// </summary>
        public async Task TestConnectionAsync()
        {
            if (!_isConnected)
                await ConnectAsync();
                
            // Send ping message
            var pingMessage = new
            {
                type = "ping",
                serverId = _serverId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            await SendMessageAsync(pingMessage);
            
            // In a real implementation, we would wait for a pong response
            // For simplicity, we're assuming the send succeeds
        }

        /// <summary>
        /// Disconnects from the relay server
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!_isConnected)
                return;
                
            _cts.Cancel();
            
            if (_webSocket.State == WebSocketState.Open)
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect requested", CancellationToken.None);
                
            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            _isConnected = false;
        }

        /// <summary>
        /// Registers a new web session with the relay server
        /// </summary>
        /// <param name="sessionKey">Unique session key</param>
        /// <param name="sessionName">Name of the session</param>
        /// <param name="expirationMinutes">Expiration time in minutes (-1 for no expiration)</param>
        /// <param name="oneTimeSession">Whether this is a one-time session (deletes after use)</param>
        public async Task RegisterWebSessionAsync(string sessionKey, string sessionName, int expirationMinutes, bool oneTimeSession = true)
        {
            if (!_isConnected)
                await ConnectAsync();
            
            // Calculate expiration time
            DateTimeOffset? expiresAt = null;
            if (expirationMinutes > 0)
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);
            
            // Create web session info
            var sessionInfo = new WebSessionInfo
            {
                SessionKey = sessionKey,
                SessionName = sessionName,
                ServerId = _serverId,
                RelayUrl = _relayUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = expiresAt,
                OneTimeSession = oneTimeSession
            };
            
            // Add to local collection
            _activeSessions[sessionKey] = sessionInfo;
            
            // Send registration message to the relay server
            var registerMessage = new
            {
                type = "register_web_session",
                serverId = _serverId,
                sessionKey = sessionKey,
                sessionName = sessionName,
                relayUrl = _relayUrl,
                expiresAt = expiresAt?.ToUnixTimeMilliseconds(),
                oneTimeSession = oneTimeSession
            };
            
            await SendMessageAsync(registerMessage);
        }

        /// <summary>
        /// Gets the URL for a web session
        /// </summary>
        /// <param name="sessionKey">The session key</param>
        /// <returns>URL that clients can use to download and run the pre-configured client</returns>
        public string GetWebSessionUrl(string sessionKey)
        {
            // If web bridge is available, use that URL
            if (_webBridge != null)
            {
                return _webBridge.GetSessionUrl(sessionKey);
            }
            
            // Otherwise, fall back to direct relay URL
            return $"{GetWebRelayUrl()}/connect?session={sessionKey}";
        }
        
        /// <summary>
        /// Gets the web URL version of the relay URL (converts wss to https)
        /// </summary>
        private string GetWebRelayUrl()
        {
            // Convert WebSocket URL to HTTP URL
            if (_relayUrl.StartsWith("wss://"))
                return "https://" + _relayUrl.Substring(6);
            if (_relayUrl.StartsWith("ws://"))
                return "http://" + _relayUrl.Substring(5);
            
            return _relayUrl;
        }

        /// <summary>
        /// Sends a message to the relay server
        /// </summary>
        private async Task SendMessageAsync(object message)
        {
            string json = JsonConvert.SerializeObject(message);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
        }

        /// <summary>
        /// Background task to receive messages from the relay server
        /// </summary>
        private async Task ReceiveMessagesAsync()
        {
            try
            {
                byte[] buffer = new byte[4096];
                
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing response received",
                            CancellationToken.None);
                        _isConnected = false;
                        break;
                    }
                    
                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;
                        
                    // Process the message
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessReceivedMessageAsync(json);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                // Log or handle the exception
                Console.WriteLine($"Error in WebSocket receive loop: {ex.Message}");
                _isConnected = false;
            }
        }

        /// <summary>
        /// Processes messages received from the relay server
        /// </summary>
        private async Task ProcessReceivedMessageAsync(string json)
        {
            try
            {
                // Parse the JSON to determine the message type
                var message = JsonConvert.DeserializeAnonymousType(json, new { type = "" });
                
                switch (message.type)
                {
                    case "pong":
                        // Handle ping response
                        break;
                        
                    case "client_connected":
                        // Handle client connection notification
                        var clientConnectMessage = JsonConvert.DeserializeAnonymousType(json, new 
                        {
                            type = "",
                            clientId = "",
                            sessionKey = ""
                        });
                        
                        await HandleClientConnectionAsync(
                            clientConnectMessage.clientId,
                            clientConnectMessage.sessionKey);
                        break;
                        
                    // Handle other message types as needed
                    
                    default:
                        Console.WriteLine($"Received unknown message type: {message.type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a client connection through a web session
        /// </summary>
        private async Task HandleClientConnectionAsync(string clientId, string sessionKey)
        {
            // Check if this is a known session
            if (!_activeSessions.TryGetValue(sessionKey, out var sessionInfo))
            {
                Console.WriteLine($"Client connected with invalid or unknown session key: {sessionKey}");
                return;
            }
            
            Console.WriteLine($"Client {clientId} connected through web session {sessionKey}");
            
            // In a real implementation, this would notify the UI or trigger events
        }
        
        /// <summary>
        /// Creates a web session via the web bridge
        /// </summary>
        /// <param name="sessionName">Optional friendly name for the session</param>
        /// <param name="expiresInMinutes">Session expiration time in minutes</param>
        /// <param name="oneTimeSession">Whether session is valid for one-time use only</param>
        /// <returns>Web session information including session key and URLs</returns>
        public async Task<WebSessionInfo> CreateWebBridgeSessionAsync(
            string sessionName = null,
            int expiresInMinutes = 30,
            bool oneTimeSession = true)
        {
            if (_webBridge == null)
                throw new InvalidOperationException("Web bridge client is not configured. Please provide a webBridgeUrl in the constructor.");
            
            try
            {
                _logger?.LogInformation("Creating web session via web bridge");
                
                // Use the web bridge to create the session
                var sessionInfo = await _webBridge.CreateSessionAsync(
                    _serverId,
                    sessionName,
                    expiresInMinutes,
                    oneTimeSession);
                
                // Add to local collection
                _activeSessions[sessionInfo.SessionKey] = sessionInfo;
                
                _logger?.LogInformation("Created web session {SessionKey} via bridge", sessionInfo.SessionKey);
                
                return sessionInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to create web session via bridge");
                throw;
            }
        }
        
        /// <summary>
        /// Checks if the web bridge is available
        /// </summary>
        /// <returns>True if the web bridge is reachable</returns>
        public async Task<bool> IsWebBridgeAvailableAsync()
        {
            if (_webBridge == null)
                return false;
                
            return await _webBridge.IsAvailableAsync();
        }
    }

    /// <summary>
    /// Represents a web support session for client downloads
    /// </summary>
    public class WebSessionInfo
    {
        public string SessionKey { get; set; }
        public string SessionName { get; set; }
        public string ServerId { get; set; }
        public string RelayUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool OneTimeSession { get; set; } = true;
    }
}
