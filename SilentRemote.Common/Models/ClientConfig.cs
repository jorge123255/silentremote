using System;
using System.IO;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace SilentRemote.Common.Models
{
    /// <summary>
    /// Connection mode for client application
    /// </summary>
    public enum ClientConnectionMode
    {
        /// <summary>
        /// Direct connection to a specific server (normal mode)
        /// </summary>
        Direct,
        
        /// <summary>
        /// Session-based connection via web support session
        /// </summary>
        WebSession,
        
        /// <summary>
        /// Token-based connection (using pre-shared token)
        /// </summary>
        TokenBased
    }

    /// <summary>
    /// Configuration settings for a client application
    /// </summary>
    public class ClientConfig
    {
        /// <summary>
        /// Unique identifier for this client
        /// </summary>
        public string ClientId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Name of the client (for display purposes)
        /// </summary>
        public string ClientName { get; set; } = "SilentRemote Client";
        
        /// <summary>
        /// URL of the relay server
        /// </summary>
        public string RelayUrl { get; set; } = "wss://relay.nextcloudcyber.com";
        
        /// <summary>
        /// ID of the server to connect to
        /// </summary>
        public string ServerId { get; set; } = string.Empty;
        
        /// <summary>
        /// Authentication token for connection validation
        /// </summary>
        public string AuthToken { get; set; } = string.Empty;
        
        /// <summary>
        /// Session key for web-based client downloads
        /// </summary>
        public string SessionKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Session name for display purposes
        /// </summary>
        public string SessionName { get; set; } = "Support Session";
        
        /// <summary>
        /// Connection mode for the client
        /// </summary>
        public ClientConnectionMode ConnectionMode { get; set; } = ClientConnectionMode.Direct;
        
        /// <summary>
        /// Whether to install the client as a service
        /// </summary>
        public bool InstallAsService { get; set; } = false;
        
        /// <summary>
        /// Whether to hide window on startup
        /// </summary>
        public bool HideOnStartup { get; set; } = true;
        
        /// <summary>
        /// Whether to auto-start with system
        /// </summary>
        public bool AutoStartWithSystem { get; set; } = false;
        
        /// <summary>
        /// Whether to show connection notifications to the user
        /// </summary>
        public bool ShowNotifications { get; set; } = true;
        
        /// <summary>
        /// Whether the user must confirm connections
        /// </summary>
        public bool RequireUserConfirmation { get; set; } = true;
        
        /// <summary>
        /// Whether this is a one-time session (common for web-based downloads)
        /// </summary>
        public bool OneTimeSession { get; set; } = false;
        
        /// <summary>
        /// Save config to JSON string
        /// </summary>
        /// <returns>JSON string representation of config</returns>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
        
        /// <summary>
        /// Load config from JSON string
        /// </summary>
        /// <param name="json">JSON string</param>
        /// <returns>ClientConfig object</returns>
        public static ClientConfig? FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ClientConfig>(json);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Save config to file
        /// </summary>
        /// <param name="path">Path to save to</param>
        public void Save(string path)
        {
            File.WriteAllText(path, ToJson());
        }
        
        /// <summary>
        /// Load config from file
        /// </summary>
        /// <param name="path">Path to load from</param>
        /// <returns>ClientConfig object</returns>
        public static ClientConfig? Load(string path)
        {
            if (!File.Exists(path))
                return null;
                
            try
            {
                string json = File.ReadAllText(path);
                return FromJson(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
