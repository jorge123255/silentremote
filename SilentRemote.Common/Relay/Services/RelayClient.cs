using System.Net.WebSockets;
using SilentRemote.Common.Messages;
using SilentRemote.Common.Networking;
using SilentRemote.Common.Relay.Models;
using System.Text;
using Newtonsoft.Json;

namespace SilentRemote.Common.Relay.Services
{
    /// <summary>
    /// Client for handling relay connections and peer-to-peer communication
    /// </summary>
    public class RelayClient : IDisposable
    {
        private readonly RelayConnection _relayConnection;
        private readonly string _clientId;
        private readonly EncryptionKeyPair _encryptionKeys;
        private readonly Dictionary<string, Action<Message>> _messageHandlers = new Dictionary<string, Action<Message>>();
        
        /// <summary>
        /// ID of this client
        /// </summary>
        public string ClientId => _clientId;
        
        /// <summary>
        /// Current connection status
        /// </summary>
        public ConnectionStatus Status => _relayConnection.Status;
        
        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event EventHandler<ConnectionStatus>? StatusChanged;
        
        /// <summary>
        /// Event raised when a direct message from peer is received
        /// </summary>
        public event EventHandler<Message>? MessageReceived;

        /// <summary>
        /// Event raised when a connection request is received
        /// </summary>
        public event EventHandler<ConnectionRequestMessage>? ConnectionRequestReceived;
        
        /// <summary>
        /// Event raised when a connection ends
        /// </summary>
        public event EventHandler? ConnectionEnded;
        
        /// <summary>
        /// Create a new relay client
        /// </summary>
        /// <param name="relayUrl">URL of the relay server</param>
        /// <param name="clientId">Unique ID for this client</param>
        public RelayClient(string relayUrl, string clientId)
        {
            _relayConnection = new RelayConnection(relayUrl);
            _clientId = clientId;
            _encryptionKeys = EncryptionKeyPair.Generate();
            
            // Subscribe to relay connection events
            _relayConnection.StatusChanged += (sender, status) => StatusChanged?.Invoke(this, status);
            _relayConnection.SignalingMessageReceived += HandleSignalingMessage;
        }
        
        /// <summary>
        /// Connect to the relay server
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            return await _relayConnection.ConnectAsync();
        }
        
        /// <summary>
        /// Disconnect from the relay server
        /// </summary>
        public async Task DisconnectAsync()
        {
            await _relayConnection.DisconnectAsync();
        }
        
        /// <summary>
        /// Request connection to a specific client/server
        /// </summary>
        /// <param name="targetId">ID of the target to connect to</param>
        /// <param name="authToken">Optional authentication token</param>
        public async Task RequestConnectionAsync(string targetId, string authToken = "")
        {
            var request = new ConnectionRequestMessage
            {
                RequesterId = _clientId,
                TargetId = targetId,
                AuthToken = authToken
            };
            
            // Create signaling message
            var signalingMessage = new SignalingMessage
            {
                Type = SignalingMessage.SignalingType.Connect,
                ConnectionId = Guid.NewGuid().ToString(),
                Payload = request.ToJson()
            };
            
            await _relayConnection.SendSignalingMessageAsync(signalingMessage);
        }
        
        /// <summary>
        /// Register this client to receive connections based on a shared token
        /// </summary>
        /// <param name="authToken">Authentication token to match</param>
        public async Task RegisterForTokenConnectionsAsync(string authToken)
        {
            if (string.IsNullOrEmpty(authToken))
                throw new ArgumentException("Auth token cannot be empty for token-based connections", nameof(authToken));
                
            // Create registration message
            var registration = new
            {
                type = "register_token",
                clientId = _clientId,
                token = authToken
            };
            
            // Convert to signaling message
            var signalingMessage = new SignalingMessage
            {
                Type = SignalingMessage.SignalingType.RegisterToken,
                ConnectionId = _clientId,
                Payload = JsonConvert.SerializeObject(registration)
            };
            
            await _relayConnection.SendSignalingMessageAsync(signalingMessage);
        }
        
        /// <summary>
        /// Send message to connected peer
        /// </summary>
        /// <param name="message">Message to send</param>
        public async Task SendMessageAsync(Message message)
        {
            if (_relayConnection.Status != ConnectionStatus.Connected)
                throw new InvalidOperationException("Not connected to relay server");
            
            string json = message.ToJson();
            
            // Create signaling message for data transport
            var signalingMessage = new SignalingMessage
            {
                Type = SignalingMessage.SignalingType.SessionDescription,
                ConnectionId = _clientId,
                Payload = json
            };
            
            await _relayConnection.SendSignalingMessageAsync(signalingMessage);
        }
        
        /// <summary>
        /// Register a message handler for specific message type
        /// </summary>
        /// <typeparam name="T">Type of message to handle</typeparam>
        /// <param name="handler">Handler function</param>
        public void RegisterMessageHandler<T>(Action<T> handler) where T : Message
        {
            string typeName = typeof(T).Name;
            _messageHandlers[typeName] = (message) => 
            {
                if (message is T typedMessage)
                {
                    handler(typedMessage);
                }
            };
        }
        
        private void HandleSignalingMessage(object? sender, SignalingMessage message)
        {
            switch (message.Type)
            {
                case SignalingMessage.SignalingType.Connect:
                    var connectionRequest = ConnectionRequestMessage.FromJson(message.Payload);
                    if (connectionRequest != null && connectionRequest.TargetId == _clientId)
                    {
                        ConnectionRequestReceived?.Invoke(this, connectionRequest);
                    }
                    break;
                
                case SignalingMessage.SignalingType.SessionDescription:
                    try
                    {
                        var peerMessage = Message.FromJson(message.Payload);
                        if (peerMessage != null)
                        {
                            // First route to specific handlers
                            string typeName = peerMessage.GetType().Name;
                            if (_messageHandlers.TryGetValue(typeName, out var handler))
                            {
                                handler(peerMessage);
                            }
                            
                            // Then raise general event
                            MessageReceived?.Invoke(this, peerMessage);
                        }
                    }
                    catch
                    {
                        // Ignore invalid messages
                    }
                    break;
                    
                case SignalingMessage.SignalingType.Disconnect:
                    try
                    {
                        // Parse disconnect message
                        var disconnectInfo = JsonConvert.DeserializeObject<Dictionary<string, string>>(message.Payload);
                        
                        // Notify that the connection has ended
                        ConnectionEnded?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        // Ignore invalid messages
                    }
                    break;
            }
        }
        
        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            _relayConnection.Dispose();
        }
    }
}
