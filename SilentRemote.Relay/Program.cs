using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace SilentRemote.Relay
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("SilentRemote Relay Server");
            Console.WriteLine("=========================");

            // Load configuration
            var config = LoadConfiguration();
            if (config == null)
            {
                Console.WriteLine("Failed to load configuration. Creating default config.");
                config = new RelayConfiguration
                {
                    Host = "0.0.0.0",
                    WebSocketPort = 8080,
                    HttpPort = 8081,
                    UseHttps = false,
                    PublicHost = "localhost",
                    AllowAnonymousConnections = true,
                    SessionExpirationMinutes = 30
                };
                SaveConfiguration(config);
            }

            // Print configuration
            Console.WriteLine($"Host: {config.Host}");
            Console.WriteLine($"WebSocket Port: {config.WebSocketPort}");
            Console.WriteLine($"HTTP Port: {config.HttpPort}");
            Console.WriteLine($"Public Host: {config.PublicHost}");
            Console.WriteLine($"HTTPS Enabled: {config.UseHttps}");

            try
            {
                // Create and start relay server
                var relay = new RelayServer(
                    config.Host, 
                    config.WebSocketPort, 
                    config.HttpPort, 
                    config.UseHttps);
                
                await relay.StartAsync();
                
                Console.WriteLine("Relay server is running. Press CTRL+C to stop.");
                
                // Set up cancellation
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };
                
                // Wait for cancellation
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                }
                
                // Stop the relay
                Console.WriteLine("Shutting down relay server...");
                await relay.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }
        
        /// <summary>
        /// Loads relay configuration from file
        /// </summary>
        private static RelayConfiguration LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (!File.Exists(configPath))
                    return null;
                    
                string json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<RelayConfiguration>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Saves relay configuration to file
        /// </summary>
        private static void SaveConfiguration(RelayConfiguration config)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                
                Console.WriteLine($"Configuration saved to {configPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Configuration for the relay server
    /// </summary>
    public class RelayConfiguration
    {
        /// <summary>
        /// Host to bind to (e.g. 0.0.0.0 for all interfaces)
        /// </summary>
        public string Host { get; set; }
        
        /// <summary>
        /// Port for WebSocket connections
        /// </summary>
        public int WebSocketPort { get; set; }
        
        /// <summary>
        /// Port for HTTP/HTTPS connections
        /// </summary>
        public int HttpPort { get; set; }
        
        /// <summary>
        /// Whether to use HTTPS
        /// </summary>
        public bool UseHttps { get; set; }
        
        /// <summary>
        /// Public hostname for the relay server (e.g. relay.nextcloudcyber.com)
        /// </summary>
        public string PublicHost { get; set; }
        
        /// <summary>
        /// Whether to allow anonymous connections
        /// </summary>
        public bool AllowAnonymousConnections { get; set; }
        
        /// <summary>
        /// Default session expiration time in minutes
        /// </summary>
        public int SessionExpirationMinutes { get; set; }
    }
}
