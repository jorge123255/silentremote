namespace SilentRemote.Common.Messages
{
    /// <summary>
    /// Message requesting a screen capture from the client
    /// </summary>
    public class ScreenCaptureMessage : Message
    {
        /// <summary>
        /// Quality of the screen capture (1-100)
        /// </summary>
        public int Quality { get; set; } = 60;
        
        /// <summary>
        /// Whether to capture the mouse cursor
        /// </summary>
        public bool CaptureCursor { get; set; } = true;
        
        /// <summary>
        /// Monitor index to capture (0 for primary, -1 for all monitors)
        /// </summary>
        public int MonitorIndex { get; set; } = -1;
        
        public ScreenCaptureMessage()
        {
            Type = MessageType.ScreenCapture;
        }
    }
    
    /// <summary>
    /// Response message containing screen capture data
    /// </summary>
    public class ScreenCaptureResponseMessage : Message
    {
        /// <summary>
        /// The captured screen image as JPEG byte array
        /// </summary>
        public byte[] ImageData { get; set; }
        
        /// <summary>
        /// Width of the screen
        /// </summary>
        public int Width { get; set; }
        
        /// <summary>
        /// Height of the screen
        /// </summary>
        public int Height { get; set; }
        
        /// <summary>
        /// Monitor index that was captured
        /// </summary>
        public int MonitorIndex { get; set; }
        
        public ScreenCaptureResponseMessage()
        {
            Type = MessageType.ScreenCaptureResponse;
            ImageData = Array.Empty<byte>();
        }
    }
}
