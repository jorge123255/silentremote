using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SilentRemote.Common.Models;
using SilentRemote.Common.Messages;
using SilentRemote.Common.Relay.Services;
using SilentRemote.Common.Relay.Models;
using SilentRemote.Common.RemoteControl;
using WindowsInput;
using WindowsInput.Native;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SilentRemote.Client
{
    class Program
    {
        // Config path - in production this would be embedded in the executable
        private static string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        
        // Client configuration
        private static ClientConfig _config = new ClientConfig();
        
        // Relay client for communication
        private static RelayClient? _relayClient;
        
        // Input simulator for keyboard and mouse control
        private static readonly InputSimulator _inputSimulator = new InputSimulator();
        
        // Cancellation token source for graceful shutdown
        private static readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        
        // Flag to hide console window
        private static bool _hideWindow = true;

        #region Native Methods
        
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        
        #endregion

        static async Task Main(string[] args)
        {
            // Parse command line arguments
            ParseArguments(args);

            // Hide console window if needed
            if (_hideWindow)
            {
                var handle = GetConsoleWindow();
                ShowWindow(handle, SW_HIDE);
            }
            else
            {
                Console.WriteLine("SilentRemote Client Starting...");
            }
            
            // Load configuration
            LoadConfiguration();
            
            // Setup graceful shutdown
            Console.CancelKeyPress += (sender, e) => 
            {
                e.Cancel = true;
                _cancellationTokenSource.Cancel();
            };
            
            // Connect to relay server and wait for commands
            await ConnectAndProcessCommands();
        }
        
        private static void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLower() == "--show-window" || args[i].ToLower() == "-s")
                {
                    _hideWindow = false;
                }
                else if (args[i].ToLower() == "--config" || args[i].ToLower() == "-c")
                {
                    if (i + 1 < args.Length)
                    {
                        _configPath = args[i + 1];
                        i++; // Skip next argument
                    }
                }
            }
        }
        
        private static void LoadConfiguration()
        {
            try
            {
                var loadedConfig = ClientConfig.Load(_configPath);
                if (loadedConfig != null)
                {
                    _config = loadedConfig;
                }
                else if (_hideWindow)
                {
                    // Failed to load config, show window if hidden
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                    Console.WriteLine($"Error: Could not load configuration from {_configPath}");
                }
            }
            catch (Exception ex)
            {
                if (_hideWindow)
                {
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                }
                
                Console.WriteLine($"Error loading configuration: {ex.Message}");
            }
        }
        
        private static async Task ConnectAndProcessCommands()
        {
            try
            {
                // Create relay client with config
                _relayClient = new RelayClient(_config.RelayUrl, _config.ClientId);
                
                // Register message handlers
                RegisterMessageHandlers();
                
                // Subscribe to relay status changes
                _relayClient.StatusChanged += (sender, status) => 
                {
                    if (!_hideWindow)
                    {
                        Console.WriteLine($"Relay connection status: {status}");
                    }
                };
                
                // Connect to relay server
                if (!_hideWindow) Console.WriteLine("Connecting to relay server...");
                
                bool connected = await _relayClient.ConnectAsync();
                
                if (!connected)
                {
                    if (_hideWindow)
                    {
                        var handle = GetConsoleWindow();
                        ShowWindow(handle, SW_SHOW);
                        _hideWindow = false;
                    }
                    Console.WriteLine("Failed to connect to relay server.");
                    return;
                }
                
                if (!_hideWindow) Console.WriteLine("Connected to relay server.");
                
                // Handle different connection modes
                switch (_config.ConnectionMode)
                {
                    case ClientConnectionMode.WebSession:
                        await HandleWebSessionConnection();
                        break;
                        
                    case ClientConnectionMode.TokenBased:
                        await HandleTokenBasedConnection();
                        break;
                        
                    case ClientConnectionMode.Direct:
                    default:
                        await HandleDirectConnection();
                        break;
                }
                
                // Keep the application running until cancellation is requested
                await Task.Delay(Timeout.Infinite, _cancellationTokenSource.Token).ContinueWith(t => { });
            }
            catch (TaskCanceledException)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                if (_hideWindow)
                {
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                }
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Cleanup
                _relayClient?.Dispose();
            }
        }
        
        /// <summary>
        /// Handles direct connection to a specific server
        /// </summary>
        private static async Task HandleDirectConnection()
        {
            if (!string.IsNullOrEmpty(_config.ServerId))
            {
                if (!_hideWindow) Console.WriteLine($"Connecting directly to server {_config.ServerId}");
                await _relayClient.RequestConnectionAsync(_config.ServerId, _config.AuthToken);
                if (!_hideWindow) Console.WriteLine($"Requested connection to server {_config.ServerId}");
            }
            else
            {
                if (_hideWindow)
                {
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                }
                Console.WriteLine("Error: No server ID specified for direct connection.");
            }
        }
        
        /// <summary>
        /// Handles web-based session connection
        /// </summary>
        private static async Task HandleWebSessionConnection()
        {
            if (!string.IsNullOrEmpty(_config.SessionKey) && !string.IsNullOrEmpty(_config.ServerId))
            {
                if (!_hideWindow) Console.WriteLine($"Connecting to web session {_config.SessionKey}");
                
                // For web sessions, we use a special format for the auth token: "session:KEY"
                string sessionAuthToken = $"session:{_config.SessionKey}";
                
                // Show a notification if configured
                if (_config.ShowNotifications)
                {
                    // In a real implementation, this would display a toast notification
                    // For now, if the window is visible, we'll display a message
                    if (!_hideWindow)
                    {
                        Console.WriteLine($"Connecting to support session: {_config.SessionName}");
                    }
                    else
                    {
                        // Show window briefly to notify the user
                        var handle = GetConsoleWindow();
                        ShowWindow(handle, SW_SHOW);
                        
                        Console.WriteLine($"Connecting to support session: {_config.SessionName}");
                        Console.WriteLine($"This window will hide in 5 seconds...");
                        
                        // Hide window after 5 seconds
                        _ = Task.Delay(5000).ContinueWith(_ => 
                        {
                            if (_hideWindow) ShowWindow(handle, SW_HIDE);
                        });
                    }
                }
                
                // Request connection using the session key as authorization
                await _relayClient.RequestConnectionAsync(_config.ServerId, sessionAuthToken);
                
                // If this is a one-time session, schedule cleanup on exit
                if (_config.OneTimeSession)
                {
                    // When the connection ends, delete the config file
                    _relayClient.ConnectionEnded += (s, e) =>
                    {
                        try
                        {
                            if (File.Exists(_configPath))
                            {
                                File.Delete(_configPath);
                            }
                        }
                        catch { /* Ignore cleanup errors */ }
                        
                        // Also initiate shutdown
                        _cancellationTokenSource.Cancel();
                    };
                }
            }
            else
            {
                if (_hideWindow)
                {
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                }
                Console.WriteLine("Error: Invalid web session configuration.");
            }
        }
        
        /// <summary>
        /// Handles token-based connection
        /// </summary>
        private static async Task HandleTokenBasedConnection()
        {
            if (!string.IsNullOrEmpty(_config.AuthToken))
            {
                // In this mode, the client connects to any server that provides a matching token
                if (!_hideWindow) Console.WriteLine("Waiting for a server with matching token...");
                
                // We don't specify a server ID, just provide the token and wait
                await _relayClient.RegisterForTokenConnectionsAsync(_config.AuthToken);
            }
            else
            {                
                if (_hideWindow)
                {
                    var handle = GetConsoleWindow();
                    ShowWindow(handle, SW_SHOW);
                    _hideWindow = false;
                }
                Console.WriteLine("Error: No auth token specified for token-based connection.");
            }
        }

        private static void RegisterMessageHandlers()
        {
            if (_relayClient == null) return;
            
            // Handle screen capture requests
            _relayClient.RegisterMessageHandler<ScreenCaptureMessage>(HandleScreenCaptureRequest);
            
            // Handle mouse movement commands
            _relayClient.RegisterMessageHandler<MouseMoveMessage>(HandleMouseMove);
            
            // Handle mouse click commands
            _relayClient.RegisterMessageHandler<MouseClickMessage>(HandleMouseClick);
            
            // Handle mouse scroll commands
            _relayClient.RegisterMessageHandler<MouseScrollMessage>(HandleMouseScroll);
            
            // Handle keyboard commands
            _relayClient.RegisterMessageHandler<KeyPressMessage>(HandleKeyPress);
            _relayClient.RegisterMessageHandler<KeyDownMessage>(HandleKeyDown);
            _relayClient.RegisterMessageHandler<KeyUpMessage>(HandleKeyUp);
        }
        
        #region Message Handlers
        
        private static async void HandleScreenCaptureRequest(ScreenCaptureMessage message)
        {
            try
            {
                // Capture the screen
                byte[] screenData = ScreenCapture.CaptureScreen(message.Quality);
                
                // Get approximate screen dimensions using more platform-agnostic approach
                int screenWidth = 1920;  // Default fallback
                int screenHeight = 1080; // Default fallback
                
                // Use platform detection for screen metrics
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // This would use P/Invoke to get actual screen dimensions on Windows
                    // For now, using default values
                }
                
                // Create response message
                var response = new ScreenCaptureResponseMessage
                {
                    ImageData = screenData,
                    Width = screenWidth,
                    Height = screenHeight,
                    MonitorIndex = message.MonitorIndex
                };
                
                // Send response
                if (_relayClient != null)
                {
                    await _relayClient.SendMessageAsync(response);
                }
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error capturing screen: {ex.Message}");
                }
            }
        }
        
        private static void HandleMouseMove(MouseMoveMessage message)
        {
            try
            {
                if (message.Absolute)
                {
                    // Use approximate screen dimensions for absolute positioning
                    double screenWidth = 1920;  // Default fallback 
                    double screenHeight = 1080; // Default fallback
                    
                    _inputSimulator.Mouse.MoveMouseTo(
                        message.X / screenWidth * 65535, 
                        message.Y / screenHeight * 65535);
                }
                else
                {
                    _inputSimulator.Mouse.MoveMouseBy(message.X, message.Y);
                }
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling mouse move: {ex.Message}");
                }
            }
        }
        
        private static void HandleMouseClick(MouseClickMessage message)
        {
            try
            {
                // Move to position if specified
                if (message.X.HasValue && message.Y.HasValue)
                {
                    // Use approximate screen dimensions for absolute positioning
                    double screenWidth = 1920;  // Default fallback 
                    double screenHeight = 1080; // Default fallback
                    
                    _inputSimulator.Mouse.MoveMouseTo(
                        message.X.Value / screenWidth * 65535,
                        message.Y.Value / screenHeight * 65535);
                }
                
                // Perform click
                switch (message.Button)
                {
                    case MouseClickMessage.MouseButton.Left:
                        if (message.DoubleClick)
                        {
                            _inputSimulator.Mouse.LeftButtonDoubleClick();
                        }
                        else
                        {
                            _inputSimulator.Mouse.LeftButtonClick();
                        }
                        break;
                        
                    case MouseClickMessage.MouseButton.Right:
                        if (message.DoubleClick)
                        {
                            _inputSimulator.Mouse.RightButtonDoubleClick();
                        }
                        else
                        {
                            _inputSimulator.Mouse.RightButtonClick();
                        }
                        break;
                        
                    case MouseClickMessage.MouseButton.Middle:
                        // InputSimulator library is limited, simulating middle click using alternate approach
                        // This presses the middle button as a virtual key since direct middle mouse methods aren't available
                        _inputSimulator.Keyboard.KeyPress(VirtualKeyCode.MBUTTON);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling mouse click: {ex.Message}");
                }
            }
        }
        
        private static void HandleMouseScroll(MouseScrollMessage message)
        {
            try
            {
                if (message.DeltaY != 0)
                {
                    _inputSimulator.Mouse.VerticalScroll(message.DeltaY);
                }
                
                if (message.DeltaX != 0)
                {
                    _inputSimulator.Mouse.HorizontalScroll(message.DeltaX);
                }
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling mouse scroll: {ex.Message}");
                }
            }
        }
        
        private static void HandleKeyPress(KeyPressMessage message)
        {
            try
            {
                if (message.Character.HasValue)
                {
                    _inputSimulator.Keyboard.TextEntry(message.Character.Value);
                }
                else
                {
                    _inputSimulator.Keyboard.KeyPress((VirtualKeyCode)message.KeyCode);
                }
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling key press: {ex.Message}");
                }
            }
        }
        
        private static void HandleKeyDown(KeyDownMessage message)
        {
            try
            {
                _inputSimulator.Keyboard.KeyDown((VirtualKeyCode)message.KeyCode);
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling key down: {ex.Message}");
                }
            }
        }
        
        private static void HandleKeyUp(KeyUpMessage message)
        {
            try
            {
                _inputSimulator.Keyboard.KeyUp((VirtualKeyCode)message.KeyCode);
            }
            catch (Exception ex)
            {
                if (!_hideWindow)
                {
                    Console.WriteLine($"Error handling key up: {ex.Message}");
                }
            }
        }
        
        #endregion
    }
}
