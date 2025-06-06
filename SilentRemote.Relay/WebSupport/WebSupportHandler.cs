using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SilentRemote.Relay.WebSupport
{
    /// <summary>
    /// Handles HTTP requests for the web support portal, serving both the landing page
    /// and client downloads based on session keys
    /// </summary>
    public class WebSupportHandler
    {
        private readonly string _contentRoot;
        private readonly HttpListener _listener;
        private readonly Dictionary<string, WebSessionInfo> _sessions;
        private readonly ClientBuilderService _clientBuilder;
        private bool _isRunning;
        private CancellationTokenSource _cts;

        public WebSupportHandler(string contentRoot, string hostUrl)
        {
            _contentRoot = contentRoot;
            _listener = new HttpListener();
            _listener.Prefixes.Add(hostUrl);
            _sessions = new Dictionary<string, WebSessionInfo>();
            _clientBuilder = new ClientBuilderService();
        }

        /// <summary>
        /// Registers a new web session that clients can use to download pre-configured clients
        /// </summary>
        public void RegisterWebSession(WebSessionInfo sessionInfo)
        {
            _sessions[sessionInfo.SessionKey] = sessionInfo;
            Console.WriteLine($"Registered web session: {sessionInfo.SessionKey} for server {sessionInfo.ServerId}");
        }

        /// <summary>
        /// Starts the web support handler
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            _cts = new CancellationTokenSource();
            _listener.Start();
            _isRunning = true;

            // Start handling requests in the background
            Task.Run(() => HandleRequests(_cts.Token));
            
            Console.WriteLine("Web support handler started");
        }

        /// <summary>
        /// Stops the web support handler
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _cts.Cancel();
            _listener.Stop();
            _isRunning = false;
            
            Console.WriteLine("Web support handler stopped");
        }

        /// <summary>
        /// Background task to handle incoming HTTP requests
        /// </summary>
        private async Task HandleRequests(CancellationToken cancellationToken)
        {
            try
            {
                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    
                    // Process the request in a separate task to allow handling multiple concurrent requests
                    _ = Task.Run(() => ProcessRequest(context), cancellationToken);
                }
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in request handling loop: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes a single HTTP request
        /// </summary>
        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath;
                string query = context.Request.Url.Query;
                
                Console.WriteLine($"Request: {path}{query}");
                
                // Parse the session key from the query
                string sessionKey = null;
                if (!string.IsNullOrEmpty(query) && query.Contains("session="))
                {
                    int startIndex = query.IndexOf("session=") + 8;
                    int endIndex = query.IndexOf('&', startIndex);
                    if (endIndex < 0) endIndex = query.Length;
                    sessionKey = query.Substring(startIndex, endIndex - startIndex);
                }
                
                // Handle different request paths
                switch (path.ToLowerInvariant())
                {
                    case "/connect":
                        await HandleConnectRequest(context, sessionKey);
                        break;
                        
                    case "/download":
                        await HandleDownloadRequest(context, sessionKey);
                        break;
                        
                    case "/api/status":
                        await HandleStatusRequest(context, sessionKey);
                        break;
                        
                    default:
                        await ServeStaticContent(context, path);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing request: {ex.Message}");
                
                try
                {
                    // Return an error response
                    context.Response.StatusCode = 500;
                    context.Response.ContentType = "text/plain";
                    byte[] buffer = Encoding.UTF8.GetBytes("Server Error");
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                catch
                {
                    // Ignore errors while sending error response
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        /// <summary>
        /// Handles requests to the /connect endpoint that serves the landing page
        /// </summary>
        private async Task HandleConnectRequest(HttpListenerContext context, string sessionKey)
        {
            // Check if the session exists
            if (string.IsNullOrEmpty(sessionKey) || !_sessions.ContainsKey(sessionKey))
            {
                await ServeInvalidSessionPage(context);
                return;
            }
            
            // Get the session info
            var sessionInfo = _sessions[sessionKey];
            
            // Check if the session has expired
            if (sessionInfo.ExpiresAt.HasValue && DateTimeOffset.UtcNow > sessionInfo.ExpiresAt.Value)
            {
                await ServeExpiredSessionPage(context);
                return;
            }
            
            // Serve the landing page
            await ServeLandingPage(context, sessionInfo);
        }

        /// <summary>
        /// Handles requests to the /download endpoint that serves client downloads
        /// </summary>
        private async Task HandleDownloadRequest(HttpListenerContext context, string sessionKey)
        {
            // Check if the session exists
            if (string.IsNullOrEmpty(sessionKey) || !_sessions.ContainsKey(sessionKey))
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/plain";
                byte[] buffer = Encoding.UTF8.GetBytes("Invalid session key");
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
                return;
            }
            
            // Get the session info
            var sessionInfo = _sessions[sessionKey];
            
            // Check if the session has expired
            if (sessionInfo.ExpiresAt.HasValue && DateTimeOffset.UtcNow > sessionInfo.ExpiresAt.Value)
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/plain";
                byte[] buffer = Encoding.UTF8.GetBytes("Session has expired");
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
                return;
            }
            
            // Get the platform from the request
            string platform = context.Request.QueryString["platform"] ?? DetectPlatform(context.Request.UserAgent);
            string clientName = context.Request.QueryString["name"] ?? "Remote Support Client";
            
            // Build the client for this platform and session
            try
            {
                // In a production app, this would be done asynchronously or pre-built clients would be cached
                byte[] clientPackage = await BuildClientForSession(sessionInfo, platform, clientName);
                
                // Serve the client as a download
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/octet-stream";
                
                string fileName = GetClientFileName(platform);
                context.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                
                await context.Response.OutputStream.WriteAsync(clientPackage, 0, clientPackage.Length);
                context.Response.Close();
                
                Console.WriteLine($"Served client download for session {sessionKey}, platform: {platform}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building client: {ex.Message}");
                
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/plain";
                byte[] buffer = Encoding.UTF8.GetBytes("Error building client");
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
            }
        }

        /// <summary>
        /// Handles requests to the /api/status endpoint that checks session status
        /// </summary>
        private async Task HandleStatusRequest(HttpListenerContext context, string sessionKey)
        {
            bool isValid = !string.IsNullOrEmpty(sessionKey) && _sessions.TryGetValue(sessionKey, out var session) && 
                          (!session.ExpiresAt.HasValue || DateTimeOffset.UtcNow <= session.ExpiresAt.Value);
            
            var response = new
            {
                valid = isValid,
                sessionName = isValid ? session.SessionName : null,
                serverId = isValid ? session.ServerId : null,
                expires = isValid && session.ExpiresAt.HasValue ? session.ExpiresAt.Value.ToUnixTimeMilliseconds() : -1
            };
            
            // Serialize the response using DataContractJsonSerializer for compatibility
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(response.GetType());
            MemoryStream ms = new MemoryStream();
            using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(ms))
            {
                serializer.WriteObject(writer, response);
                writer.Flush();
            }
            
            byte[] jsonBytes = ms.ToArray();
            
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Serves static content from the content root
        /// </summary>
        private async Task ServeStaticContent(HttpListenerContext context, string path)
        {
            // Default to index.html for root path
            if (path == "/" || string.IsNullOrEmpty(path))
                path = "/index.html";
                
            // Map the path to a file in the content root
            string filePath = Path.Combine(_contentRoot, path.TrimStart('/'));
            
            // Check if the file exists
            if (!File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/plain";
                byte[] buffer = Encoding.UTF8.GetBytes("File not found");
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
                return;
            }
            
            // Get the content type based on file extension
            string contentType = GetContentType(Path.GetExtension(filePath));
            
            // Read and serve the file
            byte[] fileData = await File.ReadAllBytesAsync(filePath);
            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            await context.Response.OutputStream.WriteAsync(fileData, 0, fileData.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Serves the landing page with embedded session information
        /// </summary>
        private async Task ServeLandingPage(HttpListenerContext context, WebSessionInfo sessionInfo)
        {
            string html = await File.ReadAllTextAsync(Path.Combine(_contentRoot, "landing.html"));
            
            // Replace placeholders with session information
            html = html.Replace("{{SESSION_KEY}}", sessionInfo.SessionKey)
                       .Replace("{{SESSION_NAME}}", sessionInfo.SessionName)
                       .Replace("{{SERVER_ID}}", sessionInfo.ServerId);
            
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Serves a page indicating the session is invalid
        /// </summary>
        private async Task ServeInvalidSessionPage(HttpListenerContext context)
        {
            string html = await File.ReadAllTextAsync(Path.Combine(_contentRoot, "invalid_session.html"));
            
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Serves a page indicating the session has expired
        /// </summary>
        private async Task ServeExpiredSessionPage(HttpListenerContext context)
        {
            string html = await File.ReadAllTextAsync(Path.Combine(_contentRoot, "expired_session.html"));
            
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        /// <summary>
        /// Detects the platform from the user agent
        /// </summary>
        private string DetectPlatform(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent))
                return "win-x64"; // Default to Windows

            userAgent = userAgent.ToLowerInvariant();
            
            if (userAgent.Contains("windows"))
                return "win-x64";
            if (userAgent.Contains("macintosh") || userAgent.Contains("mac os"))
                return "osx-x64";
            if (userAgent.Contains("linux") && !userAgent.Contains("android"))
                return "linux-x64";
                
            return "win-x64"; // Default to Windows if unknown
        }

        /// <summary>
        /// Gets the content type for a file extension
        /// </summary>
        private string GetContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Builds a client for a specific session and platform
        /// </summary>
        private async Task<byte[]> BuildClientForSession(WebSessionInfo sessionInfo, string platform, string clientName)
        {
            // This is a simplified version; in production this would use the ClientBuilder service
            
            // Create client config for the session with web session mode
            var clientConfig = new ClientConfig
            {
                ClientId = Guid.NewGuid().ToString(),
                ServerId = sessionInfo.ServerId,
                RelayUrl = sessionInfo.RelayUrl ?? "wss://relay.nextcloudcyber.com", // Use the session's relay URL if available
                AuthToken = sessionInfo.SessionKey, // For backward compatibility
                SessionKey = sessionInfo.SessionKey, // Store the session key specifically
                ConnectionMode = ClientConnectionMode.WebSession, // Set connection mode to web session
                SessionName = sessionInfo.SessionName ?? "Remote Support Session", // Use friendly session name
                ClientName = clientName,
                HideOnStartup = true, // Hide initially but show notification
                ShowNotifications = true, // Show notifications to user
                RequireUserConfirmation = false, // Auto-accept connection for web clients
                OneTimeSession = true, // Set as one-time session that will clean up after disconnect
                AutoStartWithSystem = false // Web clients shouldn't auto-start
            };
            
            // Log the configuration being used
            Console.WriteLine($"Building web client for session {sessionInfo.SessionKey} on platform {platform}");
            
            // Build the client
            string outputPath = await _clientBuilder.BuildClientAsync(
                clientName, 
                platform, 
                clientConfig);
                
            // Read the client package as bytes
            return await File.ReadAllBytesAsync(outputPath);
        }

        /// <summary>
        /// Gets the appropriate filename for a client download based on platform
        /// </summary>
        private string GetClientFileName(string platform)
        {
            return platform switch
            {
                "win-x64" => "RemoteSupport.exe",
                "osx-x64" => "RemoteSupport",
                "linux-x64" => "RemoteSupport",
                _ => "RemoteSupport.exe"
            };
        }

        /// <summary>
        /// Simple service for building clients in the relay component
        /// </summary>
        private class ClientBuilderService
        {
            // Simplified version; in production this would use the actual ClientBuilder logic
            public async Task<string> BuildClientAsync(string clientName, string platform, ClientConfig config)
            {
                // In a real implementation, this would build and package the client
                // For now, just return a placeholder file path
                string tempPath = Path.Combine(Path.GetTempPath(), $"RemoteSupport_{platform}_{Guid.NewGuid()}.zip");
                
                // Create a dummy file since we can't actually build clients in this context
                await File.WriteAllBytesAsync(tempPath, new byte[1024]); // 1KB dummy file
                
                return tempPath;
            }
        }
    }

    /// <summary>
    /// Represents a web support session for client downloads
    /// </summary>
    public class WebSessionInfo
    {
        public string SessionKey { get; set; }
        public string SessionName { get; set; }
        public string ServerId { get; set; }
        public string RelayUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool OneTimeSession { get; set; } = true;
    }

    // Extension method for DateTimeOffset in .NET 4.5.2 compatibility mode (since we found issues with this in Quasar)
    public static class DateTimeOffsetExtensions
    {
        public static long ToUnixTimeMilliseconds(this DateTimeOffset dateTimeOffset)
        {
            return (long)(dateTimeOffset.UtcDateTime - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
    }
}
