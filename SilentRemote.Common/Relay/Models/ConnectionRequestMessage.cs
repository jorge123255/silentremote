using Newtonsoft.Json;

namespace SilentRemote.Common.Relay.Models
{
    /// <summary>
    /// Message used to request a connection to a specific client/server through the relay
    /// </summary>
    public class ConnectionRequestMessage
    {
        /// <summary>
        /// Unique ID of the client to connect to
        /// </summary>
        public string TargetId { get; set; } = string.Empty;
        
        /// <summary>
        /// Unique ID of the requester
        /// </summary>
        public string RequesterId { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional authentication token
        /// </summary>
        public string AuthToken { get; set; } = string.Empty;
        
        /// <summary>
        /// Time when the request was created
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
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
        /// <returns>ConnectionRequestMessage object</returns>
        public static ConnectionRequestMessage? FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ConnectionRequestMessage>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
