namespace SilentRemote.Common.Messages
{
    /// <summary>
    /// Base class for all input control messages
    /// </summary>
    public abstract class InputControlMessage : Message
    {
    }

    /// <summary>
    /// Message to move the mouse cursor
    /// </summary>
    public class MouseMoveMessage : InputControlMessage
    {
        /// <summary>
        /// X coordinate
        /// </summary>
        public int X { get; set; }
        
        /// <summary>
        /// Y coordinate
        /// </summary>
        public int Y { get; set; }
        
        /// <summary>
        /// Whether coordinates are absolute (true) or relative (false)
        /// </summary>
        public bool Absolute { get; set; } = true;
        
        public MouseMoveMessage()
        {
            Type = MessageType.MouseMove;
        }
    }
    
    /// <summary>
    /// Message to perform a mouse click
    /// </summary>
    public class MouseClickMessage : InputControlMessage
    {
        /// <summary>
        /// Possible mouse buttons
        /// </summary>
        public enum MouseButton
        {
            Left,
            Right,
            Middle
        }
        
        /// <summary>
        /// Button to click
        /// </summary>
        public MouseButton Button { get; set; } = MouseButton.Left;
        
        /// <summary>
        /// Whether this is a double-click
        /// </summary>
        public bool DoubleClick { get; set; } = false;
        
        /// <summary>
        /// X coordinate (optional, if not set current position is used)
        /// </summary>
        public int? X { get; set; }
        
        /// <summary>
        /// Y coordinate (optional, if not set current position is used)
        /// </summary>
        public int? Y { get; set; }
        
        public MouseClickMessage()
        {
            Type = MessageType.MouseClick;
        }
    }
    
    /// <summary>
    /// Message to scroll the mouse wheel
    /// </summary>
    public class MouseScrollMessage : InputControlMessage
    {
        /// <summary>
        /// Delta for vertical scrolling
        /// </summary>
        public int DeltaY { get; set; }
        
        /// <summary>
        /// Delta for horizontal scrolling
        /// </summary>
        public int DeltaX { get; set; }
        
        public MouseScrollMessage()
        {
            Type = MessageType.MouseScroll;
        }
    }
    
    /// <summary>
    /// Message to press and release a key
    /// </summary>
    public class KeyPressMessage : InputControlMessage
    {
        /// <summary>
        /// Virtual key code
        /// </summary>
        public ushort KeyCode { get; set; }
        
        /// <summary>
        /// Unicode character (optional)
        /// </summary>
        public char? Character { get; set; }
        
        public KeyPressMessage()
        {
            Type = MessageType.KeyPress;
        }
    }
    
    /// <summary>
    /// Message to press a key down
    /// </summary>
    public class KeyDownMessage : InputControlMessage
    {
        /// <summary>
        /// Virtual key code
        /// </summary>
        public ushort KeyCode { get; set; }
        
        public KeyDownMessage()
        {
            Type = MessageType.KeyDown;
        }
    }
    
    /// <summary>
    /// Message to release a key
    /// </summary>
    public class KeyUpMessage : InputControlMessage
    {
        /// <summary>
        /// Virtual key code
        /// </summary>
        public ushort KeyCode { get; set; }
        
        public KeyUpMessage()
        {
            Type = MessageType.KeyUp;
        }
    }
}
