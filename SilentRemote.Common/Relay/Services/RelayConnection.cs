using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using SilentRemote.Common.Relay.Models;

namespace SilentRemote.Common.Relay.Services
{
    /// <summary>
    /// Handles the connection to a relay server using WebSockets
    /// </summary>
    public class RelayConnection : IDisposable
    {
        private ClientWebSocket? _webSocket;
        private readonly Uri _relayUri;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly byte[] _receiveBuffer;
        private bool _isConnected = false;
        
        /// <summary>
        /// Current status of the connection
        /// </summary>
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Initializing;
        
        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event EventHandler<ConnectionStatus>? StatusChanged;
        
        /// <summary>
        /// Event raised when a signaling message is received
        /// </summary>
        public event EventHandler<SignalingMessage>? SignalingMessageReceived;
        
        /// <summary>
        /// Create a new relay connection
        /// </summary>
        /// <param name="relayUrl">URL of the relay server (e.g. wss://relay.example.com)</param>
        public RelayConnection(string relayUrl)
        {
            // Don't create the WebSocket yet - we'll create it in ConnectAsync
            _relayUri = new Uri(relayUrl);
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveBuffer = new byte[8192]; // 8KB buffer
        }
        
        /// <summary>
        /// Connect to the relay server
        /// </summary>
        /// <returns>True if connected successfully, false otherwise</returns>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                // Clean up any existing WebSocket instance first
                await CleanupWebSocketAsync();
                
                // Create a fresh WebSocket instance
                _webSocket = new ClientWebSocket();
                
                // Configure WebSocket options for more reliable connections
                // Increase keep-alive interval for better connection stability
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                
                // Set standard HTTP headers
                _webSocket.Options.SetRequestHeader("User-Agent", "SilentRemote/1.0");
                
                // Always bypass SSL validation when connecting to localhost or internal IPs
                // This helps with self-signed certificates during development
                if (_relayUri.Host == "localhost" || _relayUri.Host == "127.0.0.1" || 
                    _relayUri.Host.EndsWith(".local") || _relayUri.Host.Contains("quasar") ||
                    _relayUri.Host.StartsWith("192.168.") || _relayUri.Host.StartsWith("10."))
                {
                    Console.WriteLine($"Bypassing SSL validation for {_relayUri.Host}");
                    _webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                }
#if DEBUG
                // In debug builds, bypass SSL validation for all connections
                else
                {
                    Console.WriteLine("DEBUG build: Bypassing SSL validation for all hosts");
                    _webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                }
#endif
                
                // Try multiple WebSocket subprotocols that might be used by the relay server
                _webSocket.Options.AddSubProtocol("json"); // Primary subprotocol
                _webSocket.Options.AddSubProtocol("quasar-protocol"); // Try custom protocol
                
                // Set essential headers for WebSocket upgrade via NGINX
                // These headers are required to properly handle WebSocket proxying in NGINX
                _webSocket.Options.SetRequestHeader("Connection", "Upgrade");
                _webSocket.Options.SetRequestHeader("Upgrade", "websocket");
                _webSocket.Options.SetRequestHeader("Origin", _relayUri.Scheme + "://" + _relayUri.Host);
                
                // Add additional headers for better proxy compatibility
                _webSocket.Options.SetRequestHeader("X-Forwarded-Host", _relayUri.Host);
                _webSocket.Options.SetRequestHeader("X-Real-IP", "127.0.0.1");
                
                Console.WriteLine($"Connecting to relay server at {_relayUri} with {_relayUri.Scheme} protocol");
                
                // Set a reasonable timeout for connection - increase from 30s to 45s for slower networks
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token, timeoutCts.Token))
                {
                    // Try to connect with the timeout
                    try
                    {
                        Console.WriteLine($"Starting WebSocket connection attempt to {_relayUri}");
                        await _webSocket.ConnectAsync(_relayUri, linkedCts.Token);
                        Console.WriteLine($"Successfully connected to {_relayUri}");
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        Console.WriteLine($"Connection timeout: {_relayUri}");
                        throw new TimeoutException($"Connection to relay server timed out after 30 seconds: {_relayUri}");
                    }
                }
                
                _isConnected = true;
                UpdateStatus(ConnectionStatus.Connected);
                
                // Start receiving messages
                _ = Task.Run(ReceiveLoop, _cancellationTokenSource.Token);
                
                Console.WriteLine($"Successfully connected to relay server: {_relayUri}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to relay server: {ex.Message}");
                UpdateStatus(ConnectionStatus.Failed);
                return false;
            }
        }
        
        /// <summary>
        /// Send a signaling message to the relay server
        /// </summary>
        /// <param name="message">Message to send</param>
        public async Task SendSignalingMessageAsync(SignalingMessage message)
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to relay server");
            
            string json = message.ToJson();
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                _cancellationTokenSource.Token);
        }
        
        /// <summary>
        /// Disconnect from the relay server
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                // Update status first to prevent new messages from being sent
                UpdateStatus(ConnectionStatus.Disconnecting);
                
                // Use our cleanup method to handle the WebSocket closure properly
                await CleanupWebSocketAsync();
                
                // Update final status
                UpdateStatus(ConnectionStatus.Disconnected);
                
                Console.WriteLine("Disconnected from relay server");
            }
            catch (Exception ex)
            {
                // Log but don't throw
                Console.WriteLine($"Error during disconnect: {ex.Message}");
                UpdateStatus(ConnectionStatus.Failed);
            }
        }
        
        private async Task ReceiveLoop()
        {
            try
            {
                Console.WriteLine("Starting WebSocket receive loop");
                
                while (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        // Create a timeout token for receive operation
                        using (var receiveTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            _cancellationTokenSource.Token, receiveTimeoutCts.Token))
                        {
                            // Use the combined token for receiving
                            WebSocketReceiveResult? result = null;
                            try
                            {
                                result = await _webSocket.ReceiveAsync(
                                    new ArraySegment<byte>(_receiveBuffer),
                                    linkedCts.Token);
                            }
                            catch (OperationCanceledException) when (receiveTimeoutCts.IsCancellationRequested)
                            {
                                // We hit a timeout in the receive operation
                                if (_webSocket.State == WebSocketState.Open)
                                {
                                    Console.WriteLine("WebSocket receive timed out, but connection is still open");
                                    continue; // Try again
                                }
                                else
                                {
                                    Console.WriteLine($"WebSocket receive timed out and connection is in state: {_webSocket.State}");
                                    throw new TimeoutException("WebSocket receive timed out and connection is not open");
                                }
                            }
                            
                            if (result == null)
                            {
                                Console.WriteLine("Received null WebSocket result, skipping");
                                continue;
                            }

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                Console.WriteLine($"Received WebSocket close message with status: {result.CloseStatus}");
                                await CleanupWebSocketAsync();
                                return;
                            }
                            
                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                string json = Encoding.UTF8.GetString(_receiveBuffer, 0, result.Count);
                                Console.WriteLine($"Received message: {(json.Length > 100 ? json.Substring(0, 100) + "..." : json)}");
                                
                                var message = SignalingMessage.FromJson(json);
                                
                                if (message != null)
                                {
                                    SignalingMessageReceived?.Invoke(this, message);
                                }
                                else
                                {
                                    Console.WriteLine("Received invalid signaling message format");
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.WriteLine($"Error during WebSocket receive: {ex.Message}");
                        
                        // Check if we should continue receiving or if the connection is broken
                        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
                        {
                            throw; // Rethrow to exit the loop
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"WebSocket receive loop terminated due to error: {ex.Message}");
            }
            finally
            {
                // Ensure we clean up resources and update state
                _isConnected = false;
                UpdateStatus(ConnectionStatus.Disconnected);
                Console.WriteLine("WebSocket receive loop ended");
            }
        }
        
        private void UpdateStatus(ConnectionStatus newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(this, newStatus);
        }
        
        /// <summary>
        /// Safely cleans up the WebSocket instance
        /// </summary>
        private async Task CleanupWebSocketAsync()
        {
            if (_webSocket != null)
            {
                try
                {
                    // Only close if the WebSocket is connected
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        // Try to close the connection gracefully with a timeout
                        using (var closeTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        {
                            try
                            {
                                await _webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure,
                                    "Closing connection",
                                    closeTimeoutCts.Token);
                            }
                            catch (Exception ex)
                            {
                                // Log but don't rethrow - we're cleaning up
                                Console.WriteLine($"Error closing WebSocket: {ex.Message}");
                            }
                        }
                    }
                    
                    // Always dispose
                    _webSocket.Dispose();
                    _webSocket = null;
                }
                catch (Exception ex)
                {
                    // Log but don't rethrow - we're cleaning up
                    Console.WriteLine($"Error during WebSocket cleanup: {ex.Message}");
                    // Still set to null to allow creating a new one
                    _webSocket = null;
                }
            }
            
            // Reset connection state
            _isConnected = false;
        }
        
        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            
            // Use the cleanup method to dispose WebSocket
            CleanupWebSocketAsync().GetAwaiter().GetResult();
        }
    }
}
