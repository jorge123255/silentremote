using System;

namespace SilentRemote.Common.Models
{
    /// <summary>
    /// Configuration for the SilentRemote server
    /// </summary>
    public class ServerConfig
    {
        /// <summary>
        /// Unique identifier for this server
        /// </summary>
        public string ServerId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Authentication token for relay communication
        /// </summary>
        public string AuthToken { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Web bridge URL for client sessions (optional)
        /// </summary>
        public string WebBridgeUrl { get; set; } = "http://web-bridge.nextcloudcyber.com:8443";
        
        /// <summary>
        /// Path to the client project for building custom clients
        /// </summary>
        public string ClientProjectPath { get; set; } = "SilentRemote.Client";
        
        /// <summary>
        /// Output directory for client builds
        /// </summary>
        public string OutputDirectory { get; set; } = "builds";
        
        /// <summary>
        /// Relay server WebSocket URL
        /// </summary>
        public string RelayUrl { get; set; } = "wss://relay.nextcloudcyber.com";
        
        /// <summary>
        /// Whether to allow automatic client connections without user confirmation
        /// </summary>
        public bool AllowAutomaticConnections { get; set; } = false;
        
        /// <summary>
        /// Whether to show notifications for connection events
        /// </summary>
        public bool ShowNotifications { get; set; } = true;
        
        /// <summary>
        /// Path to the client build template directory
        /// </summary>
        public string ClientBuildPath { get; set; } = "./client-build";
    }
}
