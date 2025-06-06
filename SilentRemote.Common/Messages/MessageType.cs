namespace SilentRemote.Common.Messages
{
    /// <summary>
    /// Defines all possible message types exchanged between client and server
    /// </summary>
    public enum MessageType
    {
        // Connection messages
        Authentication,
        AuthenticationResponse,
        KeepAlive,
        
        // Screen control messages
        ScreenCapture,
        ScreenCaptureResponse,
        
        // Mouse and keyboard control messages
        MouseMove,
        MouseClick,
        MouseScroll,
        KeyPress,
        KeyDown,
        KeyUp,
        
        // System commands
        ProcessList,
        ProcessListResponse,
        StartProcess,
        StartProcessResponse,
        KillProcess,
        KillProcessResponse,
        
        // File transfer
        FileList,
        FileListResponse,
        DownloadFile,
        DownloadFileResponse,
        UploadFile,
        UploadFileResponse,
        
        // Client management
        ClientConfig,
        ClientConfigResponse,
        Uninstall,
        Restart,
        
        // Command execution
        ExecuteCommand,
        ExecuteCommandResponse
    }
}
