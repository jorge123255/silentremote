using Newtonsoft.Json;

namespace SilentRemote.Common.Messages
{
    /// <summary>
    /// Base class for all messages exchanged between client and server
    /// </summary>
    public abstract class Message
    {
        /// <summary>
        /// Type of the message
        /// </summary>
        public MessageType Type { get; set; }
        
        /// <summary>
        /// Unique identifier for the message
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Timestamp of when the message was created
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Convert message to JSON string
        /// </summary>
        /// <returns>JSON representation of the message</returns>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, 
                new JsonSerializerSettings 
                { 
                    TypeNameHandling = TypeNameHandling.All 
                });
        }
        
        /// <summary>
        /// Convert JSON string to message object
        /// </summary>
        /// <param name="json">JSON string</param>
        /// <returns>Message object</returns>
        public static Message? FromJson(string json)
        {
            return JsonConvert.DeserializeObject<Message>(json, 
                new JsonSerializerSettings 
                { 
                    TypeNameHandling = TypeNameHandling.All 
                });
        }
    }
}
