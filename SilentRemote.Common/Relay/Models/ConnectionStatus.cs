namespace SilentRemote.Common.Relay.Models
{
    /// <summary>
    /// Represents the current status of a relay connection
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// Connection is being initialized
        /// </summary>
        Initializing,
        
        /// <summary>
        /// Connection has been established
        /// </summary>
        Connected,
        
        /// <summary>
        /// Connection failed to establish
        /// </summary>
        Failed,
        
        /// <summary>
        /// Connection is in the process of disconnecting
        /// </summary>
        Disconnecting,
        
        /// <summary>
        /// Connection has been disconnected
        /// </summary>
        Disconnected
    }
}
