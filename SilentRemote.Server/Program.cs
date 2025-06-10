using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using SilentRemote.Common.Messages;
using SilentRemote.Common.Models;
using SilentRemote.Common.Relay.Models;
using SilentRemote.Common.Relay.Services;
using SilentRemote.Server.Services;

namespace SilentRemote.Server
{
    class Program
    {
        // Configuration
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serverconfig.json");
        private static string OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clients");
        private static string ClientProjectPath = "../SilentRemote.Client/SilentRemote.Client.csproj";
        
        // Server configuration
        private static string ServerId = "RemoteServer"; // Fixed server ID instead of random GUID
        private static string RelayUrl = "wss://relay.nextcloudcyber.com"; // Base URL
        private static string AuthToken = string.Empty;
        
        // Connected clients
        private static readonly ConcurrentDictionary<string, RelayClient> ConnectedClients = new ConcurrentDictionary<string, RelayClient>();
        
        // Client being remotely controlled (if any)
        private static string? _activeClientId = null;
        
        // Cancellation token source for graceful shutdown
        private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        
        // Client builder service
        private static ClientBuilder? _clientBuilder;
        
        // Relay client for server
        private static RelayClient? _serverRelayClient;
        
        // Build and starts the app
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<AppMain>()
                .UsePlatformDetect()
                .LogToTrace();

        static async Task Main(string[] args)
        {
            // Check if we should run in console mode
            if (args.Contains("--console") || args.Contains("-c"))
            {
                await RunConsoleMode(args);
                return;
            }
            
            // Default to GUI mode
            try 
            {
                // Start the Avalonia UI application
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start UI: {ex.Message}");
                Console.WriteLine("Falling back to console mode...");
                await RunConsoleMode(args);
            }
        }
        
        private static async Task RunConsoleMode(string[] args)
        {
            Console.WriteLine("SilentRemote Server Starting...");
            
            // Load configuration
            LoadConfiguration();
            
            Console.WriteLine($"Server ID: {ServerId}");
            Console.WriteLine($"Relay URL: {RelayUrl}");
            
            // Setup graceful shutdown
            Console.CancelKeyPress += (sender, e) => 
            {
                e.Cancel = true;
                Console.WriteLine("Shutting down...");
                _cancellationTokenSource.Cancel();
            };
            
            // Initialize client builder
            _clientBuilder = new ClientBuilder(ClientProjectPath, OutputDir);
            
            // Connect to relay server
            await ConnectToRelayServer();
            
            // Start command processing loop
            await ProcessCommandsAsync();
        }
        
        private static void LoadConfiguration()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    Console.WriteLine($"Loading configuration from {ConfigPath}");
                    
                    var json = File.ReadAllText(ConfigPath);
                    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    
                    if (config != null)
                    {
                        if (config.TryGetValue("ServerId", out var serverId) && !string.IsNullOrEmpty(serverId))
                        {
                            ServerId = serverId;
                        }
                        
                        if (config.TryGetValue("RelayUrl", out var relayUrl) && !string.IsNullOrEmpty(relayUrl))
                        {
                            RelayUrl = relayUrl;
                        }
                        
                        if (config.TryGetValue("AuthToken", out var authToken) && !string.IsNullOrEmpty(authToken))
                        {
                            AuthToken = authToken;
                        }
                        
                        if (config.TryGetValue("ClientBuildPath", out var clientBuildPath) && !string.IsNullOrEmpty(clientBuildPath))
                        {
                            ClientProjectPath = clientBuildPath;
                        }
                        
                        if (config.TryGetValue("ClientOutputPath", out var clientOutputPath) && !string.IsNullOrEmpty(clientOutputPath))
                        {
                            OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, clientOutputPath);
                            
                            // Ensure output directory exists
                            if (!Directory.Exists(OutputDir))
                            {
                                Directory.CreateDirectory(OutputDir);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Configuration file not found at {ConfigPath}, using default settings");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine("Using default settings");
            }
        }
        
        private static async Task ConnectToRelayServer()
        {
            try
            {
                Console.WriteLine("Connecting to relay server...");
                
                // Initialize relay client with proper error handling
                // Helper function to generate URL variations for a base URL
                List<string> GenerateUrlVariations(string baseUrl)
                {
                    // Parse the URL to get components
                    Uri uri;
                    try {
                        uri = new Uri(baseUrl);
                    } catch {
                        return new List<string> { baseUrl }; // Can't parse, just return original
                    }
                    
                    var result = new List<string>();
                    var host = uri.Host;
                    var scheme = uri.Scheme;
                    var port = uri.Port;
                    
                    // Base URL with explicit paths
                    var paths = new[] { "", "/", "/server", "/signaling" };
                    
                    // Try multiple protocol + port combinations
                    foreach (var protocol in new[] { "wss://", "ws://" })
                    {
                        foreach (var path in paths)
                        {
                            // Try standard port
                            result.Add($"{protocol}{host}{path}");
                            
                            // Try explicit ports for WebSockets
                            foreach (var explicitPort in new[] { 443, 8443, 8080, 80 })
                            {
                                result.Add($"{protocol}{host}:{explicitPort}{path}");
                            }
                        }
                    }
                    
                    return result;
                }
                
                // Get the selected relay URL - try multiple connection options
                // Log which URLs we're trying to use for diagnostics
                Console.WriteLine("Attempting to connect to relay server with multiple URL options...");
                
                // Allow overriding the relay URL with a command line or config option
                // RelayUrl = "ws://localhost:8080"; // Uncomment to override with a specific URL
                
                // Direct connections to containers - try both dev and production container names
                var directContainerUrls = new List<string>
                {
                    // Development container connections
                    "ws://localhost:8080", // Docker port mapping (most reliable)
                    "ws://127.0.0.1:8080", // Alternative localhost IP
                    "ws://quasar-signaling:8080", // Development container name
                    
                    // Production container names (from docker-compose.production.yml)
                    "ws://quasar-relay-1:8080", // Production container name 1
                    "ws://quasar-relay-2:8080", // Production container name 2
                    "ws://relay_backend:8080", // Production upstream name
                    
                    // Service names (as defined in docker-compose files)
                    "ws://signaling:8080", // Service name from docker-compose.yml
                    
                    // Fallbacks to web-bridge connections
                    "ws://localhost:8443", // Web bridge port mapping
                    "ws://quasar-web-bridge:3000", // Web bridge container
                    
                    // NGINX proxy routes - both http and https
                    "ws://localhost:80/", // Root WebSocket path
                    "ws://localhost:80/ws", // Legacy WebSocket path
                    "wss://localhost:443/", // HTTPS root path
                    "wss://localhost:443/ws", // HTTPS legacy path
                    
                    // Full domain-based URLs
                    "wss://relay.nextcloudcyber.com", // Production domain with WSS
                    "ws://relay.nextcloudcyber.com" // Without encryption
                };
                
                // Generate URL variations from the base relay URL
                var baseRelayUrls = new List<string>
                {
                    RelayUrl, // User configured URL
                    "wss://relay.nextcloudcyber.com", // Primary relay URL
                    "ws://relay.nextcloudcyber.com" // Backup non-secure URL
                };
                
                // Start with direct connections first
                var urlsToTry = new List<string>(directContainerUrls);
                
                // Then add variations for all base URLs
                foreach (var baseUrl in baseRelayUrls)
                {
                    urlsToTry.AddRange(GenerateUrlVariations(baseUrl));
                }
                
                // Add IP-based URLs as fallback
                urlsToTry.AddRange(new[]
                {
                    // From the user's Docker information - direct container access
                    "ws://localhost:8080",  // quasar-signaling
                    "ws://f6a28bde0a24:8080", // container ID for signaling
                    "ws://quasar-signaling:8080", // docker service name
                    "ws://localhost:8443", // quasar-web-bridge
                    "ws://a1b074877e60:3000", // container ID and internal port for web-bridge
                    "ws://quasar-web-bridge:3000" // docker service name and internal port
                });
                
                // Remove duplicates while preserving order
                urlsToTry = urlsToTry.Distinct().ToList();
                Console.WriteLine($"Generated {urlsToTry.Count} URL variants to try");
                foreach (var url in urlsToTry.Take(5))
                {
                    Console.WriteLine($"  URL option: {url}");
                }
                
                RelayClient? relayClient = null;
                bool connected = false;
                Exception? lastException = null;
                
                // Try each URL with retries
                foreach (string url in urlsToTry)
                {
                    if (connected) break;
                    
                    Console.WriteLine($"Trying to connect using URL: {url}");
                    
                    // Make sure to dispose any previous client
                    if (relayClient != null)
                    {
                        try 
                        { 
                            relayClient.Dispose(); 
                            relayClient = null; 
                        } 
                        catch (Exception ex) 
                        { 
                            Console.WriteLine($"Error disposing relay client: {ex.Message}"); 
                        }
                    }
                    
                    // Create a fresh client for each URL
                    relayClient = new RelayClient(url, ServerId);
                    
                    // Setup event handlers
                    EventHandler<ConnectionStatus>? statusHandler = null;
                    EventHandler<ConnectionRequestMessage>? requestHandler = null;
                    
                    statusHandler = (sender, status) => 
                    {
                        Console.WriteLine($"Server relay connection status: {status}");
                        
                        if (status == ConnectionStatus.Connected)
                        {
                            try
                            {
                                Console.WriteLine("Successfully connected to relay server and updated status");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error updating UI with connection status: {ex.Message}");
                            }
                        }
                    };
                    
                    requestHandler = async (sender, request) => 
                    {
                        Console.WriteLine($"Connection request received from: {request.RequesterId}");
                        await AcceptClientConnection(request);
                    };
                    
                    relayClient.StatusChanged += statusHandler;
                    relayClient.ConnectionRequestReceived += requestHandler;
                    
                    // Try to connect with multiple attempts and smarter backoff
                    int maxRetries = 5; // Increased from 3 to 5 for more patience
                    Random jitter = new Random();
                    
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            Console.WriteLine($"Connection attempt {i+1} of {maxRetries} to {url}");
                            
                            // Set WebSocket options if available (through constructor or property)
                            try
                            {
                                // We may need to modify RelayClient to expose these options
                                // This is a conceptual placeholder that would need proper implementation
                                // relayClient.SetWebSocketOptions(5000, true);  // 5 sec timeout, keep alive
                            }
                            catch (Exception optEx)
                            {
                                Console.WriteLine($"Failed to set WebSocket options: {optEx.Message}");
                            }
                            
                            // Connect with timeout
                            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                            {
                                var connectTask = relayClient.ConnectAsync();
                                var completedTask = await Task.WhenAny(connectTask, Task.Delay(30000, timeoutCts.Token));
                                
                                if (completedTask == connectTask)
                                {
                                    // Connection attempt completed (success or failure)
                                    connected = await connectTask; // Get the result
                                }
                                else
                                {
                                    // Connection timed out
                                    Console.WriteLine("Connection attempt timed out after 30 seconds");
                                    connected = false;
                                }
                            }
                            
                            if (connected)
                            {
                                Console.WriteLine($"Successfully connected to relay server at {url}");
                                _serverRelayClient = relayClient; // Only save the successful connection
                                break;
                            }
                            else
                            {
                                Console.WriteLine($"Connection attempt {i+1} failed, retrying after delay...");
                                
                                // Clean up and create a fresh client instance for retry
                                relayClient.Dispose();
                                relayClient = new RelayClient(url, ServerId);
                                relayClient.StatusChanged += statusHandler;
                                relayClient.ConnectionRequestReceived += requestHandler;
                                
                                // Exponential backoff with jitter to prevent thundering herd
                                int baseDelay = 1000; // 1 second base
                                int maxDelay = 30000; // 30 seconds max
                                int exponentialDelay = Math.Min(maxDelay, baseDelay * (int)Math.Pow(2, i));
                                int jitterDelay = jitter.Next(-(exponentialDelay / 4), exponentialDelay / 4);
                                int totalDelay = exponentialDelay + jitterDelay;
                                
                                Console.WriteLine($"Waiting {totalDelay/1000.0:F1} seconds before retry {i+2}...");
                                await Task.Delay(totalDelay);
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            Console.WriteLine($"Connection attempt {i+1} error: {ex.Message}");
                            
                            // Clean up and create a fresh client instance for retry
                            try
                            {
                                relayClient.Dispose();
                            }
                            catch (Exception disposeEx)
                            {
                                Console.WriteLine($"Error during client dispose: {disposeEx.Message}");
                            }
                            
                            relayClient = new RelayClient(url, ServerId);
                            relayClient.StatusChanged += statusHandler;
                            relayClient.ConnectionRequestReceived += requestHandler;
                            
                            // Exponential backoff with jitter for exceptions too
                            int baseDelay = 2000; // 2 second base for exceptions
                            int maxDelay = 30000; // 30 seconds max
                            int exponentialDelay = Math.Min(maxDelay, baseDelay * (int)Math.Pow(2, i));
                            int jitterDelay = jitter.Next(-(exponentialDelay / 4), exponentialDelay / 4);
                            int totalDelay = exponentialDelay + jitterDelay;
                            
                            Console.WriteLine($"Waiting {totalDelay/1000.0:F1} seconds before retry {i+2} after error...");
                            await Task.Delay(totalDelay);
                        }
                    }
                }
                
                if (!connected)
                {
                    string errorMessage = lastException != null ? 
                        $"Failed to connect to relay server: {lastException.Message}" :
                        "Failed to connect to relay server after multiple attempts";
                    Console.WriteLine(errorMessage);
                    return;
                }
                
                Console.WriteLine("Connected to relay server successfully");
                Console.WriteLine("Server is ready to accept client connections.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to relay server: {ex.Message}");
            }
        }
        
        private static async Task AcceptClientConnection(ConnectionRequestMessage request)
        {
            try
            {
                // Create a relay client for the connection
                var clientRelayClient = new RelayClient(RelayUrl, Guid.NewGuid().ToString());
                
                // Register message handlers
                RegisterMessageHandlers(clientRelayClient);
                
                // Connect to relay
                bool connected = await clientRelayClient.ConnectAsync();
                if (!connected)
                {
                    Console.WriteLine($"Failed to establish connection to client {request.RequesterId}");
                    return;
                }
                
                // Request connection to the client
                await clientRelayClient.RequestConnectionAsync(request.RequesterId);
                
                // Add to connected clients
                ConnectedClients[request.RequesterId] = clientRelayClient;
                
                Console.WriteLine($"Client connected: {request.RequesterId}");
                
                // If no active client, automatically select this one
                if (_activeClientId == null)
                {
                    _activeClientId = request.RequesterId;
                    Console.WriteLine($"Selected client {request.RequesterId} for remote control");
                    
                    // Start screen capture loop
                    _ = Task.Run(() => StartScreenCaptureLoopAsync(request.RequesterId));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting client connection: {ex.Message}");
            }
        }
        
        private static void RegisterMessageHandlers(RelayClient client)
        {
            // Register screen capture response handler
            client.RegisterMessageHandler<ScreenCaptureResponseMessage>(message => 
            {
                // Handle screen capture data (in a real app, this would update UI)
                Console.WriteLine($"Received screen capture: {message.Width}x{message.Height}, {message.ImageData.Length} bytes");
                
                // Process screen capture data
                ProcessScreenCapture(message);
            });
        }
        
        private static void ProcessScreenCapture(ScreenCaptureResponseMessage message)
        {
            // In a real application, this would update a UI window with the captured screen
            // For this console demo, we just acknowledge receiving the capture data
            
            // You would typically:
            // 1. Decompress the image data
            // 2. Display it in a form or window
            // 3. Allow user interaction with the image to send mouse/keyboard commands
        }
        
        private static async Task StartScreenCaptureLoopAsync(string clientId)
        {
            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested && _activeClientId == clientId)
                {
                    // Check if we still have the client connection
                    if (!ConnectedClients.TryGetValue(clientId, out var client))
                    {
                        Console.WriteLine($"Client {clientId} disconnected, stopping screen capture");
                        _activeClientId = null;
                        break;
                    }
                    
                    // Create screen capture request
                    var screenCaptureRequest = new ScreenCaptureMessage
                    {
                        Quality = 70, // Medium quality for performance
                        CaptureCursor = true,
                        MonitorIndex = -1 // All monitors
                    };
                    
                    // Send request
                    await client.SendMessageAsync(screenCaptureRequest);
                    
                    // Wait before next capture (adjust based on performance needs)
                    await Task.Delay(200, _cancellationTokenSource.Token); // 5 FPS
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in screen capture loop: {ex.Message}");
            }
        }
        
        private static async Task ProcessCommandsAsync()
        {
            bool running = true;
            
            Console.WriteLine("\nCommands:");
            Console.WriteLine("  help - Show this help");
            Console.WriteLine("  list - List connected clients");
            Console.WriteLine("  select <id> - Select a client to control");
            Console.WriteLine("  build - Enter build management mode to create and configure clients");
            Console.WriteLine("  config - View and modify server configuration");
            Console.WriteLine("  mouse <x> <y> - Move mouse to position");
            Console.WriteLine("  click <left|right|middle> - Click mouse button");
            Console.WriteLine("  type <text> - Type text");
            Console.WriteLine("  exit - Exit the application\n");
            
            while (running && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Console.Write("> ");
                string? command = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(command))
                    continue;
                
                string[] parts = command.Split(' ', 2);
                string cmd = parts[0].ToLower();
                string args = parts.Length > 1 ? parts[1] : string.Empty;
                
                switch (cmd)
                {
                    case "help":
                        Console.WriteLine("\nCommands:");
                        Console.WriteLine("  help - Show this help");
                        Console.WriteLine("  list - List connected clients");
                        Console.WriteLine("  select <id> - Select a client to control");
                        Console.WriteLine("  build - Enter build management mode to create and configure clients");
                        Console.WriteLine("  config - View and modify server configuration");
                        Console.WriteLine("  mouse <x> <y> - Move mouse to position");
                        Console.WriteLine("  click <left|right|middle> - Click mouse button");
                        Console.WriteLine("  type <text> - Type text");
                        Console.WriteLine("  exit - Exit the application\n");
                        break;
                        
                    case "list":
                        ListConnectedClients();
                        break;
                        
                    case "select":
                        await SelectClientAsync(args);
                        break;
                        
                    case "build":
                        await BuildClientManagerAsync();
                        break;
                        
                    case "config":
                        ConfigurationManager();
                        break;
                        
                    case "mouse":
                        await SendMouseMoveCommandAsync(args);
                        break;
                        
                    case "click":
                        await SendMouseClickCommandAsync(args);
                        break;
                        
                    case "type":
                        await SendKeyboardCommandAsync(args);
                        break;
                        
                    case "exit":
                        running = false;
                        break;
                        
                    default:
                        Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        break;
                }
            }
            
            // Clean up
            await DisconnectAllClientsAsync();
        }
        
        private static void ListConnectedClients()
        {
            if (ConnectedClients.Count == 0)
            {
                Console.WriteLine("No clients connected.");
                return;
            }
            
            Console.WriteLine("\nConnected clients:");
            foreach (var clientId in ConnectedClients.Keys)
            {
                string activeMarker = clientId == _activeClientId ? " (active)" : "";
                Console.WriteLine($" - {clientId}{activeMarker}");
            }
            Console.WriteLine();
        }
        
        private static Task SelectClientAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Console.WriteLine("Please specify a client ID.");
                return Task.CompletedTask;
            }
            
            if (!ConnectedClients.ContainsKey(clientId))
            {
                Console.WriteLine($"Client {clientId} not found.");
                return Task.CompletedTask;
            }
            
            // Update active client
            _activeClientId = clientId;
            Console.WriteLine($"Now controlling client: {clientId}");
            
            // Start screen capture for the selected client
            Task.Run(() => StartScreenCaptureLoopAsync(clientId));
            return Task.CompletedTask;
        }
        
        private static async Task BuildClientManagerAsync()
        {
            bool inBuildMode = true;
            
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("      CLIENT BUILD MANAGEMENT SYSTEM    ");
            Console.WriteLine("=========================================");
            
            while (inBuildMode && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Console.WriteLine("\nBuild Options:");
                Console.WriteLine("  1. Build new client");
                Console.WriteLine("  2. Configure relay server");
                Console.WriteLine("  3. Test relay connection");
                Console.WriteLine("  4. View recent builds");
                Console.WriteLine("  0. Return to main menu\n");
                
                Console.Write("Select an option: ");
                string? option = Console.ReadLine();
                
                switch (option)
                {
                    case "1":
                        await BuildNewClientAsync();
                        break;
                        
                    case "2":
                        ConfigureRelayServer();
                        break;
                        
                    case "3":
                        await TestRelayConnectionAsync();
                        break;
                        
                    case "4":
                        ViewRecentBuilds();
                        break;
                        
                    case "0":
                        inBuildMode = false;
                        Console.Clear();
                        break;
                        
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }
            }
        }
        
        private static async Task BuildNewClientAsync()
        {
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("            BUILD NEW CLIENT            ");
            Console.WriteLine("=========================================");
            
            // Get client name
            Console.Write("\nClient name: ");
            string? clientName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(clientName))
            {
                Console.WriteLine("Client name cannot be empty.");
                return;
            }
            
            // Get platform
            Console.WriteLine("\nSelect platform:");
            Console.WriteLine("  1. Windows (win-x64)");
            Console.WriteLine("  2. macOS (osx-x64)");
            Console.WriteLine("  3. Linux (linux-x64)");
            
            Console.Write("Platform option: ");
            string? platformOption = Console.ReadLine();
            
            string platform;
            switch (platformOption)
            {
                case "1": platform = "win-x64"; break;
                case "2": platform = "osx-x64"; break;
                case "3": platform = "linux-x64"; break;
                default:
                    Console.WriteLine("Invalid platform selection.");
                    return;
            }
            
            // Advanced options
            bool hideOnStartup = true;
            bool autoStartWithSystem = false;
            string customAuthToken = AuthToken;
            
            Console.WriteLine("\nAdvanced options (press Enter to use defaults):");
            
            Console.Write("Hide client window on startup? (Y/n): ");
            string? hideOption = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(hideOption) && hideOption.ToLower()[0] == 'n')
                hideOnStartup = false;
                
            Console.Write("Auto-start with system? (y/N): ");
            string? autoStartOption = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(autoStartOption) && autoStartOption.ToLower()[0] == 'y')
                autoStartWithSystem = true;
                
            Console.Write($"Authentication token (default: {(string.IsNullOrEmpty(AuthToken) ? "none" : AuthToken)}): ");
            string? authTokenInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(authTokenInput))
                customAuthToken = authTokenInput;
            
            // Review settings
            Console.WriteLine("\nBuild configuration:");
            Console.WriteLine($"  Client Name: {clientName}");
            Console.WriteLine($"  Platform: {platform}");
            Console.WriteLine($"  Relay URL: {RelayUrl}");
            Console.WriteLine($"  Server ID: {ServerId}");
            Console.WriteLine($"  Hide on Startup: {hideOnStartup}");
            Console.WriteLine($"  Auto-start with System: {autoStartWithSystem}");
            Console.WriteLine($"  Authentication Token: {(string.IsNullOrEmpty(customAuthToken) ? "none" : customAuthToken)}");
            
            Console.Write("\nProceed with build? (Y/n): ");
            string? proceed = Console.ReadLine();
            
            if (!string.IsNullOrWhiteSpace(proceed) && proceed.ToLower()[0] == 'n')
            {
                Console.WriteLine("Build canceled.");
                return;
            }
            
            try
            {
                Console.WriteLine($"\nBuilding client for {platform}...");
                
                // Create client configuration
                var config = new ClientConfig
                {
                    ClientId = Guid.NewGuid().ToString(),
                    ClientName = clientName,
                    RelayUrl = RelayUrl,
                    ServerId = ServerId,
                    AuthToken = customAuthToken,
                    HideOnStartup = hideOnStartup,
                    AutoStartWithSystem = autoStartWithSystem
                };
                
                // Build client
                var outputPath = await _clientBuilder!.BuildClientAsync(config, platform);
                
                Console.WriteLine($"\nClient built successfully!\n");
                Console.WriteLine($"Output: {outputPath}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError building client: {ex.Message}");
                Console.WriteLine("\nPress any key to continue...");
                Console.ReadKey(true);
            }
        }
        
        private static void ConfigureRelayServer()
        {
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("         RELAY SERVER CONFIGURATION     ");
            Console.WriteLine("=========================================");
            
            Console.WriteLine($"\nCurrent Relay URL: {RelayUrl}");
            Console.Write("New Relay URL (press Enter to keep current): ");
            string? newRelayUrl = Console.ReadLine();
            
            if (!string.IsNullOrWhiteSpace(newRelayUrl))
            {
                RelayUrl = newRelayUrl;
                
                // Update config file
                try
                {
                    var config = new Dictionary<string, string>
                    {
                        { "ServerId", ServerId },
                        { "RelayUrl", RelayUrl },
                        { "AuthToken", AuthToken },
                        { "ClientBuildPath", ClientProjectPath },
                        { "ClientOutputPath", Path.GetFileName(OutputDir) }
                    };
                    
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(ConfigPath, json);
                    
                    Console.WriteLine("\nConfiguration updated successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError saving configuration: {ex.Message}");
                }
            }
            
            Console.WriteLine($"\nCurrent Server ID: {ServerId}");
            Console.Write("New Server ID (press Enter to keep current): ");
            string? newServerId = Console.ReadLine();
            
            if (!string.IsNullOrWhiteSpace(newServerId))
            {
                ServerId = newServerId;
                
                // Update config file
                try
                {
                    var config = new Dictionary<string, string>
                    {
                        { "ServerId", ServerId },
                        { "RelayUrl", RelayUrl },
                        { "AuthToken", AuthToken },
                        { "ClientBuildPath", ClientProjectPath },
                        { "ClientOutputPath", Path.GetFileName(OutputDir) }
                    };
                    
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(ConfigPath, json);
                    
                    Console.WriteLine("\nConfiguration updated successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError saving configuration: {ex.Message}");
                }
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
        
        private static async Task TestRelayConnectionAsync()
        {
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("         TESTING RELAY CONNECTION       ");
            Console.WriteLine("=========================================");
            
            Console.WriteLine($"\nTesting connection to: {RelayUrl}");
            Console.WriteLine("Please wait...");
            
            try
            {
                var testClient = new RelayClient(RelayUrl, "test-" + Guid.NewGuid().ToString());
                
                bool connected = await testClient.ConnectAsync();
                
                if (connected)
                {
                    Console.WriteLine("\n✅ Connection successful! The relay server is accessible.");
                    await testClient.DisconnectAsync();
                }
                else
                {
                    Console.WriteLine("\n❌ Connection failed. The relay server may be offline or unreachable.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error testing connection: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
        
        private static void ViewRecentBuilds()
        {
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("             RECENT BUILDS              ");
            Console.WriteLine("=========================================");
            
            try
            {
                if (!Directory.Exists(OutputDir))
                {
                    Console.WriteLine("\nNo builds found. Output directory does not exist.");
                }
                else
                {
                    var files = Directory.GetFiles(OutputDir, "SilentRemote_*");
                    
                    if (files.Length == 0)
                    {
                        Console.WriteLine("\nNo builds found in the output directory.");
                    }
                    else
                    {
                        Console.WriteLine($"\nFound {files.Length} client builds:\n");
                        
                        foreach (var file in files)
                        {
                            var info = new FileInfo(file);
                            Console.WriteLine($"  {info.Name}");
                            Console.WriteLine($"    Size: {(info.Length / 1024.0 / 1024.0).ToString("F1")} MB");
                            Console.WriteLine($"    Created: {info.CreationTime}");
                            Console.WriteLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError reading builds: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
        
        private static void ConfigurationManager()
        {
            Console.Clear();
            Console.WriteLine("=========================================");
            Console.WriteLine("        SERVER CONFIGURATION            ");
            Console.WriteLine("=========================================");
            
            Console.WriteLine("\nCurrent Configuration:");
            Console.WriteLine($"  Server ID: {ServerId}");
            Console.WriteLine($"  Relay URL: {RelayUrl}");
            Console.WriteLine($"  Auth Token: {AuthToken}");
            Console.WriteLine($"  Client Build Path: {ClientProjectPath}");
            Console.WriteLine($"  Client Output Path: {OutputDir}");
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey(true);
        }
        
        private static async Task SendMouseMoveCommandAsync(string args)
        {
            if (_activeClientId == null)
            {
                Console.WriteLine("No client selected. Use 'select <id>' to select a client.");
                return;
            }
            
            string[] parts = args.Split(' ');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
            {
                Console.WriteLine("Invalid arguments. Format: mouse <x> <y>");
                return;
            }
            
            if (!ConnectedClients.TryGetValue(_activeClientId, out var client))
            {
                Console.WriteLine("Selected client is no longer connected.");
                _activeClientId = null;
                return;
            }
            
            try
            {
                var mouseMoveMessage = new MouseMoveMessage
                {
                    X = x,
                    Y = y,
                    Absolute = true
                };
                
                await client.SendMessageAsync(mouseMoveMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending mouse move command: {ex.Message}");
            }
        }
        
        private static async Task SendMouseClickCommandAsync(string args)
        {
            if (_activeClientId == null)
            {
                Console.WriteLine("No client selected. Use 'select <id>' to select a client.");
                return;
            }
            
            string button = args.ToLower();
            if (!new[] { "left", "right", "middle" }.Contains(button))
            {
                Console.WriteLine("Invalid button. Valid buttons are: left, right, middle");
                return;
            }
            
            if (!ConnectedClients.TryGetValue(_activeClientId, out var client))
            {
                Console.WriteLine("Selected client is no longer connected.");
                _activeClientId = null;
                return;
            }
            
            try
            {
                var mouseClickMessage = new MouseClickMessage
                {
                    Button = button == "left" ? MouseClickMessage.MouseButton.Left :
                             button == "right" ? MouseClickMessage.MouseButton.Right :
                             MouseClickMessage.MouseButton.Middle
                };
                
                await client.SendMessageAsync(mouseClickMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending mouse click command: {ex.Message}");
            }
        }
        
        private static async Task SendKeyboardCommandAsync(string text)
        {
            if (_activeClientId == null)
            {
                Console.WriteLine("No client selected. Use 'select <id>' to select a client.");
                return;
            }
            
            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine("Text cannot be empty.");
                return;
            }
            
            if (!ConnectedClients.TryGetValue(_activeClientId, out var client))
            {
                Console.WriteLine("Selected client is no longer connected.");
                _activeClientId = null;
                return;
            }
            
            try
            {
                // Send each character as a key press
                foreach (char c in text)
                {
                    var keyPressMessage = new KeyPressMessage
                    {
                        Character = c
                    };
                    
                    await client.SendMessageAsync(keyPressMessage);
                    await Task.Delay(10); // Small delay between keypresses
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending keyboard command: {ex.Message}");
            }
        }
        
        private static async Task DisconnectAllClientsAsync()
        {
            foreach (var client in ConnectedClients.Values)
            {
                try
                {
                    await client.DisconnectAsync();
                    client.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
            
            ConnectedClients.Clear();
            
            // Disconnect server relay client
            if (_serverRelayClient != null)
            {
                try
                {
                    await _serverRelayClient.DisconnectAsync();
                    _serverRelayClient.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }
}
