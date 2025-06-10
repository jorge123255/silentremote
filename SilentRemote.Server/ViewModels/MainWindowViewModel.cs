using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ReactiveUI;
using SilentRemote.Common.Models;
using SilentRemote.Server.Services;

namespace SilentRemote.Server.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ClientBuilder _clientBuilder;
        private readonly ServerConfig _serverConfig;
        private readonly RelaySignalingService _relayService;

        // Properties for server status
        private string _serverId;
        public string ServerId 
        { 
            get => _serverId; 
            set => this.RaiseAndSetIfChanged(ref _serverId, value); 
        }

        private string _relayUrl;
        public string RelayUrl 
        { 
            get => _relayUrl; 
            set => this.RaiseAndSetIfChanged(ref _relayUrl, value); 
        }

        private string _connectionStatus = "Disconnected";
        public string ConnectionStatus 
        { 
            get => _connectionStatus; 
            set => this.RaiseAndSetIfChanged(ref _connectionStatus, value); 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage 
        { 
            get => _statusMessage; 
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value); 
        }

        // Web Support Portal Properties
        private string _newSessionName = "Support Session";
        public string NewSessionName 
        { 
            get => _newSessionName; 
            set => this.RaiseAndSetIfChanged(ref _newSessionName, value); 
        }

        private bool _isWebLinkGenerated;
        public bool IsWebLinkGenerated 
        { 
            get => _isWebLinkGenerated; 
            set => this.RaiseAndSetIfChanged(ref _isWebLinkGenerated, value); 
        }

        private string _generatedWebLink;
        public string GeneratedWebLink 
        { 
            get => _generatedWebLink; 
            set => this.RaiseAndSetIfChanged(ref _generatedWebLink, value); 
        }

        private Bitmap _qrCodeImage = null!;
        public Bitmap QrCodeImage 
        { 
            get => _qrCodeImage; 
            set => this.RaiseAndSetIfChanged(ref _qrCodeImage, value); 
        }

        public ObservableCollection<string> ExpirationOptions { get; } = new ObservableCollection<string>
        {
            "1 hour",
            "4 hours",
            "8 hours",
            "24 hours",
            "Never (until server restart)"
        };

        private string _selectedExpiration = "8 hours";
        public string SelectedExpiration 
        { 
            get => _selectedExpiration; 
            set => this.RaiseAndSetIfChanged(ref _selectedExpiration, value); 
        }

        // Client Builder properties
        private string _clientName = "SilentRemote Client";
        public string ClientName 
        { 
            get => _clientName; 
            set => this.RaiseAndSetIfChanged(ref _clientName, value); 
        }

        private bool _isWindowsSelected = true;
        public bool IsWindowsSelected 
        { 
            get => _isWindowsSelected; 
            set => this.RaiseAndSetIfChanged(ref _isWindowsSelected, value); 
        }

        private bool _isMacSelected;
        public bool IsMacSelected 
        { 
            get => _isMacSelected; 
            set => this.RaiseAndSetIfChanged(ref _isMacSelected, value); 
        }

        private bool _isLinuxSelected;
        public bool IsLinuxSelected 
        { 
            get => _isLinuxSelected; 
            set => this.RaiseAndSetIfChanged(ref _isLinuxSelected, value); 
        }

        private bool _hideOnStartup;
        public bool HideOnStartup 
        { 
            get => _hideOnStartup; 
            set => this.RaiseAndSetIfChanged(ref _hideOnStartup, value); 
        }

        private bool _autoStartWithSystem;
        public bool AutoStartWithSystem 
        { 
            get => _autoStartWithSystem; 
            set => this.RaiseAndSetIfChanged(ref _autoStartWithSystem, value); 
        }

        private string _customRelayUrl = string.Empty;
        public string CustomRelayUrl 
        { 
            get => _customRelayUrl; 
            set => this.RaiseAndSetIfChanged(ref _customRelayUrl, value); 
        }

        private int _buildProgress;
        public int BuildProgress 
        { 
            get => _buildProgress; 
            set => this.RaiseAndSetIfChanged(ref _buildProgress, value); 
        }

        private bool _isBuildInProgress;
        public bool IsBuildInProgress 
        { 
            get => _isBuildInProgress; 
            set => this.RaiseAndSetIfChanged(ref _isBuildInProgress, value); 
        }

        private bool _isBuildComplete;
        public bool IsBuildComplete 
        { 
            get => _isBuildComplete; 
            set => this.RaiseAndSetIfChanged(ref _isBuildComplete, value); 
        }

        private string _buildOutputPath = string.Empty;
        public string BuildOutputPath 
        { 
            get => _buildOutputPath; 
            set => this.RaiseAndSetIfChanged(ref _buildOutputPath, value); 
        }

        // Commands
        public ICommand TestRelayConnectionCommand { get; }
        public ICommand ManageRelaySettingsCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand ShowAboutCommand { get; }
        public ICommand GenerateWebLinkCommand { get; }
        public ICommand CopyWebLinkCommand { get; }
        public ICommand SendEmailCommand { get; }
        public ICommand BuildCustomClientCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }

        public MainWindowViewModel()
        {
            // Initialize services
            _serverConfig = LoadServerConfig();
            // Initialize client builder with default paths
            _clientBuilder = new ClientBuilder("SilentRemote.Client", "builds");
            // Initialize relay service with web bridge URL
            _relayService = new RelaySignalingService(_serverConfig.RelayUrl, _serverConfig.ServerId, _serverConfig.AuthToken, _serverConfig.WebBridgeUrl);
            
            // Set initial values from config
            ServerId = _serverConfig.ServerId;
            RelayUrl = _serverConfig.RelayUrl;
            
            // Initialize commands
            TestRelayConnectionCommand = ReactiveCommand.CreateFromTask(TestRelayConnectionAsync);
            ManageRelaySettingsCommand = ReactiveCommand.CreateFromTask(ManageRelaySettings);
            ShowSettingsCommand = ReactiveCommand.Create(ShowSettings);
            ExitCommand = ReactiveCommand.Create(Exit);
            ShowAboutCommand = ReactiveCommand.Create(ShowAbout);
            GenerateWebLinkCommand = ReactiveCommand.CreateFromTask(GenerateWebLinkAsync);
            CopyWebLinkCommand = ReactiveCommand.Create(CopyWebLink);
            SendEmailCommand = ReactiveCommand.Create(SendEmail);
            BuildCustomClientCommand = ReactiveCommand.CreateFromTask(BuildCustomClientAsync);
            OpenOutputFolderCommand = ReactiveCommand.Create(OpenOutputFolder);
            
            // Set initial status
            StatusMessage = "Initializing...";
            ConnectionStatus = "Disconnected";
            
            // Proper async initialization pattern
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100); // Short delay to ensure UI is ready
                await InitializeRelayServiceAsync();
            });
        }

        private ServerConfig LoadServerConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serverconfig.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerConfig>(json);
                    return config ?? new ServerConfig
                    {
                        ServerId = Guid.NewGuid().ToString(),
                        RelayUrl = "wss://relay.nextcloudcyber.com",
                        WebBridgeUrl = "http://web-bridge.nextcloudcyber.com:8443",
                        AuthToken = "default-token"
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading server config: {ex.Message}");
            }
            
            // Return default config if loading fails
            return new ServerConfig
            {
                ServerId = Guid.NewGuid().ToString(),
                RelayUrl = "wss://relay.nextcloudcyber.com",
                WebBridgeUrl = "http://web-bridge.nextcloudcyber.com:8443",
                AuthToken = "default-token"
            };
        }
        
        private async Task InitializeRelayServiceAsync()
        {
            try
            {
                // Update UI safely on the UI thread
                await UpdateStatusSafelyAsync("Connecting to relay server...");
                await UpdateConnectionStatusSafelyAsync("Connecting");
                
                // Connect to relay service
                await _relayService.ConnectAsync();
                
                // Update UI with connection status
                await UpdateConnectionStatusSafelyAsync("Connected");
                await UpdateStatusSafelyAsync("Ready");
            }
            catch (Exception ex)
            {
                // Update UI with error status
                await UpdateConnectionStatusSafelyAsync("Disconnected");
                await UpdateStatusSafelyAsync($"Error connecting to relay: {ex.Message}");
                Console.WriteLine($"Relay connection error: {ex.Message}");
            }
        }
        
        private async Task TestRelayConnectionAsync()
        {
            try
            {
                // Use Avalonia's dispatcher to update UI properties from background threads
                await UpdateStatusSafelyAsync("Testing connection to relay server...");
                
                // Run the actual connection test
                await _relayService.TestConnectionAsync();
                
                // Update UI with results
                await UpdateStatusSafelyAsync("Relay connection successful");
            }
            catch (Exception ex)
            {
                // Update UI with error message
                await UpdateStatusSafelyAsync($"Relay connection failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Safely updates status message on the UI thread
        /// </summary>
        private async Task UpdateStatusSafelyAsync(string message)
        {
            // Make sure we're on the UI thread
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                StatusMessage = message;
            }
            else
            {
                // Marshal to UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = message;
                });
            }
        }
        
        /// <summary>
        /// Safely updates connection status on the UI thread
        /// </summary>
        private async Task UpdateConnectionStatusSafelyAsync(string status)
        {
            // Make sure we're on the UI thread
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                ConnectionStatus = status;
            }
            else
            {
                // Marshal to UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectionStatus = status;
                });
            }
        }

        private async Task ManageRelaySettings()
        {
            // Will be implemented with a dialog
            await UpdateStatusSafelyAsync("Relay settings dialog will appear here");
            
            // TODO: Show a proper relay settings dialog
            // This should be implemented using a proper Avalonia dialog
            // For now, we'll just update the status message
        }

        private void ShowSettings()
        {
            // Will be implemented with a dialog
            StatusMessage = "Settings dialog will appear here";
        }

        private void Exit()
        {
            Environment.Exit(0);
        }

        private void ShowAbout()
        {
            // Will be implemented with a dialog
            StatusMessage = "About dialog will appear here";
        }
        
        private async Task GenerateWebLinkAsync()
        {
            try
            {
                StatusMessage = "Generating web support session...";
                
                // Get expiration time in minutes
                int expirationMinutes = GetExpirationMinutes();
                
                // Create web session via web bridge
                var sessionInfo = await _relayService.CreateWebBridgeSessionAsync(_newSessionName, expirationMinutes);
                
                // Generate the web link using the session key
                string webUrl = _relayService.GetWebSessionUrl(sessionInfo.SessionKey);
                
                // Update the UI
                GeneratedWebLink = webUrl;
                IsWebLinkGenerated = true;
                
                // Generate QR code (placeholder for now)
                // QrCodeImage = GenerateQrCode(webUrl);
                
                StatusMessage = $"Web support session generated successfully - Session key: {sessionInfo.SessionKey}";
                Console.WriteLine($"Web URL: {webUrl}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error generating web session: {ex.Message}";
                Console.WriteLine($"Session generation error: {ex.Message}");
                
                // Fallback to direct relay session creation if web bridge fails
                try {
                    StatusMessage = "Falling back to direct relay session...";
                    string sessionKey = GenerateUniqueSessionKey();
                    await _relayService.RegisterWebSessionAsync(sessionKey, _newSessionName, GetExpirationMinutes());
                    string webUrl = _relayService.GetWebSessionUrl(sessionKey);
                    GeneratedWebLink = webUrl;
                    IsWebLinkGenerated = true;
                    StatusMessage = "Web support session generated via fallback method";
                } catch (Exception fallbackEx) {
                    StatusMessage = $"All session creation methods failed: {fallbackEx.Message}";
                }
            }
        }

        private string GenerateUniqueSessionKey()
        {
            // Generate a 6-digit random number for the session key
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private int GetExpirationMinutes()
        {
            return SelectedExpiration switch
            {
                "1 hour" => 60,
                "4 hours" => 240,
                "8 hours" => 480,
                "24 hours" => 1440,
                _ => -1 // Never expires
            };
        }

        private void CopyWebLink()
        {
            // In a real implementation, would use clipboard
            StatusMessage = "Web link copied to clipboard";
        }

        private void SendEmail()
        {
            // Will be implemented with email functionality
            StatusMessage = "Email functionality will be implemented here";
        }

        private async Task BuildCustomClientAsync()
        {
            try
            {
                // Reset state
                IsBuildComplete = false;
                IsBuildInProgress = true;
                BuildProgress = 0;
                StatusMessage = "Building custom client...";
                
                // Determine the selected platform
                string platform = "win-x64";
                if (IsMacSelected) platform = "osx-x64";
                if (IsLinuxSelected) platform = "linux-x64";
                
                // Create client config
                var clientConfig = new ClientConfig
                {
                    ClientId = Guid.NewGuid().ToString(),
                    ServerId = ServerId,
                    RelayUrl = string.IsNullOrEmpty(CustomRelayUrl) ? RelayUrl : CustomRelayUrl,
                    AuthToken = _serverConfig.AuthToken,
                    HideOnStartup = HideOnStartup,
                    AutoStartWithSystem = AutoStartWithSystem
                };
                
                // Progress reporting
                Progress<int> progress = new Progress<int>(p => BuildProgress = p);
                
                // Build the client
                string outputZipPath = await _clientBuilder.BuildClientAsync(ClientName, platform, clientConfig, _serverConfig.ClientProjectPath, _serverConfig.OutputDirectory, progress);
                
                // Update UI
                BuildOutputPath = outputZipPath;
                IsBuildComplete = true;
                StatusMessage = "Client built successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error building client: {ex.Message}";
            }
            finally
            {
                IsBuildInProgress = false;
            }
        }

        private void OpenOutputFolder()
        {
            try
            {
                if (string.IsNullOrEmpty(BuildOutputPath))
                {
                    StatusMessage = "Output path is not set";
                    return;
                }
                
                string? dirPath = Path.GetDirectoryName(BuildOutputPath);
                if (string.IsNullOrEmpty(dirPath))
                {
                    StatusMessage = "Could not determine directory path";
                    return;
                }
                
                var directoryInfo = new DirectoryInfo(dirPath);
                if (directoryInfo.Exists)
                {
                    // This would open the folder in the native file explorer
                    // For cross-platform, this needs platform-specific implementation
                    StatusMessage = $"Would open folder: {directoryInfo.FullName}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening folder: {ex.Message}";
            }
        }
    }
    
    public class ViewModelBase : ReactiveObject
    {
        // Base class for view models
    }
}
