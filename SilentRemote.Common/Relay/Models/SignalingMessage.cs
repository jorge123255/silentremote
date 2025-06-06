using Newtonsoft.Json;

namespace SilentRemote.Common.Relay.Models
{
    /// <summary>
    /// Message exchanged during signaling to establish a connection between client and server
    /// </summary>
    public class SignalingMessage
    {
        /// <summary>
        /// Type of signaling message
        /// </summary>
        public enum SignalingType
        {
            /// <summary>
            /// Request to establish a connection
            /// </summary>
            Connect,
            
            /// <summary>
            /// Response to a connection request
            /// </summary>
            ConnectResponse,
            
            /// <summary>
            /// ICE candidate for WebRTC connection
            /// </summary>
            IceCandidate,
            
            /// <summary>
            /// WebRTC session description
            /// </summary>
            SessionDescription,
            
            /// <summary>
            /// Register for token-based connections
            /// </summary>
            RegisterToken,
            
            /// <summary>
            /// Connection has been terminated
            /// </summary>
            Disconnect
        }
        
        /// <summary>
        /// Type of the signaling message
        /// </summary>
        public SignalingType Type { get; set; }
        
        /// <summary>
        /// ID of the connection
        /// </summary>
        public string ConnectionId { get; set; } = string.Empty;
        
        /// <summary>
        /// JSON data payload
        /// </summary>
        public string Payload { get; set; } = string.Empty;
        
        /// <summary>
        /// Convert to JSON string
        /// </summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
        
        /// <summary>
        /// Parse from JSON string
        /// </summary>
        /// <param name="json">JSON string to parse</param>
        /// <returns>SignalingMessage object</returns>
        public static SignalingMessage? FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<SignalingMessage>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
