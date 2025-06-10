using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SilentRemote.Common.Models;

namespace SilentRemote.Server.Services
{
    /// <summary>
    /// Represents a web support session for client downloads
    /// </summary>
    public class WebSessionInfo
    {
        public string SessionKey { get; set; } = string.Empty;
        public string? SessionName { get; set; }
        public string? ServerId { get; set; }
        public string? RelayUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
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
        private readonly WebBridgeClient _webBridge;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected;
        
        // Collection of active web sessions
        private Dictionary<string, WebSessionInfo> _activeSessions = new Dictionary<string, WebSessionInfo>();

        public RelaySignalingService(string relayUrl, string serverId, string authToken, string webBridgeUrl = null)
        {
            _relayUrl = relayUrl;
            _serverId = serverId;
            _authToken = authToken;
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            
            // Initialize web bridge client if URL is provided
            if (!string.IsNullOrEmpty(webBridgeUrl))
            {
                _webBridge = new WebBridgeClient(webBridgeUrl);
                Console.WriteLine($"Web bridge client initialized with URL: {webBridgeUrl}");
            }
        }

        /// <summary>
        /// Connects to the relay server for signaling
        /// </summary>
        public async Task ConnectAsync()
        {
            // Don't try to reconnect if we're already connected
            if (_isConnected && _webSocket != null && _webSocket.State == WebSocketState.Open)
                return;

            // Always dispose and recreate the WebSocket to ensure a clean state
            await CleanupWebSocketAsync();
            
            try
            {
                // Create a new WebSocket instance
                _webSocket = new ClientWebSocket();
                
                // Set up WebSocket options
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                _webSocket.Options.SetRequestHeader("User-Agent", "SilentRemote.Server");
                
                // Create URI with server path
                Uri serverUri = new Uri($"{_relayUrl}/server");
                Console.WriteLine($"Connecting to relay server at {serverUri}");
                
                // Ensure we have a valid cancellation token
                if (_cts == null || _cts.IsCancellationRequested)
                {
                    _cts?.Dispose();
                    _cts = new CancellationTokenSource();
                }
                else
                {
                    // Reset the cancellation token
                    _cts = new CancellationTokenSource();
                }
                
                // Set a reasonable timeout
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token))
                {
                    // Attempt connection with timeout
                    await _webSocket.ConnectAsync(serverUri, linkedCts.Token);
                }
                Console.WriteLine("WebSocket connected, sending authentication");
                
                // Send authentication message
                var authMessage = new
                {
                    type = "auth",
                    serverId = _serverId,
                    token = _authToken
                };
                
                await SendMessageAsync(authMessage);
                Console.WriteLine("Authentication message sent");
                
                // Start listening for messages in a way that won't crash the UI thread
                var _ = Task.Run(async () => 
                {
                    try 
                    {
                        await ReceiveMessagesAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in message receiving: {ex.Message}");
                    }
                });
                
                _isConnected = true;
                Console.WriteLine("Successfully connected to relay server");
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Console.WriteLine($"Connection error: {ex.Message}");
                throw new Exception($"Failed to connect to relay server: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Tests the connection to the relay server
        /// </summary>
        public async Task TestConnectionAsync()
        {
            try
            {
                // Ensure we have a fresh WebSocket connection to avoid state issues
                // Dispose existing connection if any
                if (_webSocket.State != WebSocketState.None)
                {
                    try { _webSocket.Dispose(); } catch { /* ignore errors during cleanup */ }
                    _webSocket = new ClientWebSocket();
                    _isConnected = false;
                }
                
                // Create new cancellation token to avoid using a potentially cancelled one
                _cts = new CancellationTokenSource();
                _cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10 second timeout for connection test
                
                // Connect if not already connected
                if (!_isConnected)
                {
                    await ConnectAsync();
                }
                
                // Send ping message
                var pingMessage = new
                {
                    type = "ping",
                    serverId = _serverId,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                
                await SendMessageAsync(pingMessage);
                
                // In a real implementation, we would wait for a pong response
                // For now, we're assuming the connection is successful if we get this far
            }
            catch (Exception ex)
            {
                // Reset connection state on any error
                _isConnected = false;
                throw new Exception($"Relay connection test failed: {ex.Message}", ex);
            }
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
                
            await CleanupWebSocketAsync();
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
            if (_webSocket == null || _webSocket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Cannot send message: WebSocket is not connected");
            }
            
            try
            {
                string json = JsonConvert.SerializeObject(message);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                
                // Create a new cancellation token source with a timeout to prevent hanging
                using (var sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, sendCts.Token))
                {
                    // Send with timeout protection
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(buffer), 
                        WebSocketMessageType.Text, 
                        true, // endOfMessage
                        linkedCts.Token
                    );
                    
                    Console.WriteLine($"Sent message: {message.GetType().Name}");
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Send operation timed out");
            }
            catch (WebSocketException wsEx)
            {
                _isConnected = false;
                throw new Exception($"WebSocket error while sending message: {wsEx.Message}", wsEx);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send message: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Background task to receive messages from the relay server
        /// </summary>
        private async Task ReceiveMessagesAsync()
        {
            const int bufferSize = 8192; // Larger buffer for messages
            byte[] buffer = new byte[bufferSize];
            
            try
            {
                Console.WriteLine("Starting message receiver loop");
                
                // Keep receiving until cancelled or connection closed
                while (!_cts.Token.IsCancellationRequested && 
                       _webSocket != null && 
                       _webSocket.State == WebSocketState.Open)
                {
                    // Use a timeout for receive operations to prevent hanging
                    using (var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(30))) // 30-second timeout
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, receiveCts.Token))
                    {
                        try
                        {
                            WebSocketReceiveResult result = await _webSocket.ReceiveAsync(
                                new ArraySegment<byte>(buffer), 
                                linkedCts.Token
                            );
                            
                            // Handle close message
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Console.WriteLine("Received close message from server");
                                try
                                {
                                    await _webSocket.CloseAsync(
                                        WebSocketCloseStatus.NormalClosure,
                                        "Closing response received",
                                        CancellationToken.None);
                                }
                                catch (Exception closeEx)
                                {
                                    Console.WriteLine($"Error during WebSocket close: {closeEx.Message}");
                                }
                                
                                _isConnected = false;
                                break;
                            }
                            
                            // Skip non-text messages
                            if (result.MessageType != WebSocketMessageType.Text)
                            {
                                Console.WriteLine($"Skipping non-text message: {result.MessageType}");
                                continue;
                            }
                            
                            // Process the received text message
                            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            Console.WriteLine($"Received message: {json.Substring(0, Math.Min(100, json.Length))}...");
                            
                            await ProcessReceivedMessageAsync(json);
                        }
                        catch (OperationCanceledException)
                        {
                            // Just a timeout, continue receiving
                            Console.WriteLine("Receive operation timed out, continuing...");
                            continue;
                        }
                        catch (WebSocketException wsEx) 
                        {
                            Console.WriteLine($"WebSocket error during receive: {wsEx.Message}");
                            _isConnected = false;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any unexpected errors
                _isConnected = false;
                Console.WriteLine($"Fatal error in message receiver: {ex.Message}");
            }
            finally
            {
                // Ensure we clean up connection state
                _isConnected = false;
                Console.WriteLine("Message receiver stopped");
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
                Console.WriteLine("Creating web session via web bridge");
                
                // Use the web bridge to create the session
                var sessionInfo = await _webBridge.CreateSessionAsync(
                    _serverId,
                    sessionName,
                    expiresInMinutes,
                    oneTimeSession);
                
                // Add to local collection
                _activeSessions[sessionInfo.SessionKey] = sessionInfo;
                
                Console.WriteLine($"Created web session {sessionInfo.SessionKey} via bridge");
                
                return sessionInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create web session via bridge: {ex.Message}");
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
        
        /// <summary>
        /// Properly cleans up the WebSocket connection
        /// </summary>
        private async Task CleanupWebSocketAsync()
        {
            _isConnected = false;
            
            if (_webSocket != null)
            {
                try
                {
                    // If the connection is open, try to close it gracefully
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        // Use a short timeout for the close operation
                        using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        {
                            try
                            {
                                await _webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Closing connection for retry",
                                    closeCts.Token);
                                    
                                Console.WriteLine("WebSocket closed gracefully");
                            }
                            catch (Exception ex)
                            {
                                // Log but continue - we'll dispose anyway
                                Console.WriteLine($"Error during WebSocket close: {ex.Message}");
                            }
                        }
                    }
                }
                finally
                {
                    // Always dispose regardless of close success
                    try
                    { 
                        _webSocket.Dispose();
                        _webSocket = null;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing WebSocket: {ex.Message}");
                    }
                }
            }
        }
    }
}
