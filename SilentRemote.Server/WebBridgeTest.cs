using System;
using System.Threading.Tasks;
using SilentRemote.Server.Services;

namespace SilentRemote.Server
{
    /// <summary>
    /// Test utility for verifying web bridge functionality
    /// </summary>
    public class WebBridgeTest
    {
        private readonly RelaySignalingService _relayService;
        
        public WebBridgeTest(string relayUrl, string serverId, string authToken, string webBridgeUrl)
        {
            _relayService = new RelaySignalingService(
                relayUrl, 
                serverId, 
                authToken,
                webBridgeUrl);
        }
        
        /// <summary>
        /// Tests the web bridge connection and creates a test session
        /// </summary>
        public async Task<string> RunTestAsync()
        {
            Console.WriteLine("Testing web bridge connection...");
            
            bool isAvailable = await _relayService.IsWebBridgeAvailableAsync();
            if (!isAvailable)
            {
                Console.WriteLine($"ERROR: Web bridge is not available or not responding");
                return null;
            }
            
            Console.WriteLine("Web bridge is available!");
            
            try
            {
                // Create a test session
                var sessionInfo = await _relayService.CreateWebBridgeSessionAsync(
                    sessionName: "Test Session",
                    expiresInMinutes: 30,
                    oneTimeSession: true);
                
                Console.WriteLine($"Successfully created session: {sessionInfo.SessionKey}");
                Console.WriteLine($"Session URL: {_relayService.GetWebSessionUrl(sessionInfo.SessionKey)}");
                Console.WriteLine($"Using relay server: {sessionInfo.RelayUrl}");
                Console.WriteLine($"Session expires: {sessionInfo.ExpiresAt}");
                
                return _relayService.GetWebSessionUrl(sessionInfo.SessionKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to create session: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Console test runner method - not an entry point to avoid build conflicts
        /// </summary>
        public static async Task RunConsoleTest(string[] args)
        {
            // These values need to be replaced with your actual configuration
            string relayUrl = "wss://relay.nextcloudcyber.com";
            string serverId = "your-server-id";
            string authToken = "your-auth-token";
            string webBridgeUrl = "http://web-bridge.nextcloudcyber.com:8443";
            
            if (args.Length >= 4)
            {
                relayUrl = args[0];
                serverId = args[1];
                authToken = args[2];
                webBridgeUrl = args[3];
            }
            
            var test = new WebBridgeTest(relayUrl, serverId, authToken, webBridgeUrl);
            await test.RunTestAsync();
            
            Console.WriteLine("Test complete. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
