using System;
using System.Runtime.Serialization;

namespace SilentRemote.Relay.WebSupport
{
    /// <summary>
    /// Message to register a web support session with the relay
    /// </summary>
    [DataContract]
    public class RegisterWebSessionMessage
    {
        [DataMember(Name = "type")]
        public string Type { get; set; } = "register_web_session";
        
        [DataMember(Name = "sessionKey")]
        public string SessionKey { get; set; }
        
        [DataMember(Name = "sessionName")]
        public string SessionName { get; set; }
        
        [DataMember(Name = "serverId")]
        public string ServerId { get; set; }
        
        [DataMember(Name = "expiresInMinutes")]
        public int ExpiresInMinutes { get; set; } = 30;
    }
    
    /// <summary>
    /// Message confirming a web support session registration
    /// </summary>
    [DataContract]
    public class WebSessionRegisteredMessage
    {
        [DataMember(Name = "type")]
        public string Type { get; set; } = "web_session_registered";
        
        [DataMember(Name = "sessionKey")]
        public string SessionKey { get; set; }
        
        [DataMember(Name = "webUrl")]
        public string WebUrl { get; set; }
        
        [DataMember(Name = "expiresAt")]
        public long ExpiresAt { get; set; }
    }
    
    /// <summary>
    /// Message for when a client connects through a web session
    /// </summary>
    [DataContract]
    public class WebSessionClientConnectedMessage
    {
        [DataMember(Name = "type")]
        public string Type { get; set; } = "web_session_client_connected";
        
        [DataMember(Name = "sessionKey")]
        public string SessionKey { get; set; }
        
        [DataMember(Name = "clientId")]
        public string ClientId { get; set; }
        
        [DataMember(Name = "platform")]
        public string Platform { get; set; }
    }
}
