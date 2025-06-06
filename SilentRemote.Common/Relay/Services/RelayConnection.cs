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
        private readonly ClientWebSocket _webSocket;
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
            _webSocket = new ClientWebSocket();
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
                await _webSocket.ConnectAsync(_relayUri, _cancellationTokenSource.Token);
                _isConnected = true;
                UpdateStatus(ConnectionStatus.Connected);
                
                // Start receiving messages
                _ = Task.Run(ReceiveLoop, _cancellationTokenSource.Token);
                
                return true;
            }
            catch (Exception)
            {
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
            if (_isConnected)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing connection",
                        _cancellationTokenSource.Token);
                }
                catch
                {
                    // Ignore any errors during disconnect
                }
                finally
                {
                    _isConnected = false;
                    UpdateStatus(ConnectionStatus.Disconnected);
                }
            }
        }
        
        private async Task ReceiveLoop()
        {
            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(_receiveBuffer),
                        _cancellationTokenSource.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            _cancellationTokenSource.Token);
                        _isConnected = false;
                        UpdateStatus(ConnectionStatus.Disconnected);
                        return;
                    }
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(_receiveBuffer, 0, result.Count);
                        var message = SignalingMessage.FromJson(json);
                        
                        if (message != null)
                        {
                            SignalingMessageReceived?.Invoke(this, message);
                        }
                    }
                }
            }
            catch
            {
                _isConnected = false;
                UpdateStatus(ConnectionStatus.Disconnected);
            }
        }
        
        private void UpdateStatus(ConnectionStatus newStatus)
        {
            Status = newStatus;
            StatusChanged?.Invoke(this, newStatus);
        }
        
        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _webSocket.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }
}
