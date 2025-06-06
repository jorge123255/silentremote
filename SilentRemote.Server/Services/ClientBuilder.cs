using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SilentRemote.Common.Models;

namespace SilentRemote.Server.Services
{
    /// <summary>
    /// Service for building customized client applications with embedded configuration
    /// </summary>
    public class ClientBuilder
    {
        private readonly string _clientProjectPath;
        private readonly string _outputDirectory;
        
        /// <summary>
        /// Create a new client builder
        /// </summary>
        /// <param name="clientProjectPath">Path to the client project</param>
        /// <param name="outputDirectory">Directory to output built clients</param>
        public ClientBuilder(string clientProjectPath, string outputDirectory)
        {
            _clientProjectPath = clientProjectPath;
            _outputDirectory = outputDirectory;
            
            // Create output directory if it doesn't exist
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }
        }
        
        /// <summary>
        /// Build a client with the specified configuration
        /// </summary>
        /// <param name="config">Client configuration</param>
        /// <param name="platform">Target platform (e.g. win-x64, osx-x64)</param>
        /// <returns>Path to the built client</returns>
        public async Task<string> BuildClientAsync(ClientConfig config, string platform = "win-x64")
        {
            // Create unique build directory
            string buildDir = Path.Combine(_outputDirectory, $"build_{config.ClientId}");
            if (Directory.Exists(buildDir))
            {
                Directory.Delete(buildDir, true);
            }
            Directory.CreateDirectory(buildDir);
            
            // Save configuration to build directory
            string configPath = Path.Combine(buildDir, "config.json");
            config.Save(configPath);
            
            // Create publishing profile
            string publishProfilePath = Path.Combine(buildDir, "publish-profile.pubxml");
            string publishProfile = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""4.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>Any CPU</Platform>
    <PublishDir>{buildDir}</PublishDir>
    <PublishProtocol>FileSystem</PublishProtocol>
    <TargetFramework>net9.0</TargetFramework>
    <RuntimeIdentifier>{platform}</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
  </PropertyGroup>
</Project>";
            File.WriteAllText(publishProfilePath, publishProfile);
            
            // Build and publish
            var result = await RunDotNetCommand($"publish \"{_clientProjectPath}\" -p:PublishProfile=\"{publishProfilePath}\" --nologo");
            
            if (result != 0)
            {
                throw new Exception($"Failed to build client. Exit code: {result}");
            }
            
            // Determine output file based on platform
            string outputFile = Path.Combine(buildDir, platform.StartsWith("win") ? "SilentRemote.Client.exe" : "SilentRemote.Client");
            
            if (!File.Exists(outputFile))
            {
                throw new Exception("Build succeeded but output file not found.");
            }
            
            // Create client package (zip file with executable and config)
            string zipFilePath = Path.Combine(_outputDirectory, $"SilentRemote_{config.ClientName}_{platform}.zip");
            await CreateZipFileAsync(buildDir, zipFilePath);
            
            return zipFilePath;
        }
        
        /// <summary>
        /// Run a dotnet command
        /// </summary>
        /// <param name="arguments">Command arguments</param>
        /// <returns>Exit code</returns>
        private async Task<int> RunDotNetCommand(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                
                // Read output and error streams
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                await Task.WhenAll(outputTask, errorTask);
                
                // Wait for process to exit
                await process.WaitForExitAsync();
                
                // Log output
                Console.WriteLine($"Dotnet Output: {outputTask.Result}");
                
                // Log error if any
                if (!string.IsNullOrEmpty(errorTask.Result))
                {
                    Console.WriteLine($"Dotnet Error: {errorTask.Result}");
                }
                
                return process.ExitCode;
            }
        }
        
        /// <summary>
        /// Create a zip file from a directory
        /// </summary>
        /// <param name="sourceDirectory">Directory to zip</param>
        /// <param name="zipFilePath">Path to output zip file</param>
        private async Task CreateZipFileAsync(string sourceDirectory, string zipFilePath)
        {
            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }
            
            // Use system zip command for macOS/Linux
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "zip",
                    Arguments = $"-r \"{zipFilePath}\" .",
                    WorkingDirectory = sourceDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Failed to create zip file: {await process.StandardError.ReadToEndAsync()}");
                    }
                }
            }
            else
            {
                // Use .NET built-in compression for Windows
                await Task.Run(() => System.IO.Compression.ZipFile.CreateFromDirectory(
                    sourceDirectory,
                    zipFilePath,
                    System.IO.Compression.CompressionLevel.Optimal,
                    false));
            }
        }
    }
}
