using System.Runtime.InteropServices;

namespace CefHelper
{
    static internal class Native
    {

        [DllImport("user32.dll")]
        static public extern short GetKeyState(VK nVirtKey);

        static public bool IsKeyDown(VK wparam)
        {
            return (GetKeyState(wparam) & 0x8000) != 0;
        }
    }

    /// <summary>
    /// Copy from CefSharp.Wpf
    /// Windows Message Enums
    /// Gratiosly based on http://www.pinvoke.net/default.aspx/Enums/WindowsMessages.html
    /// </summary>
    public enum WM
    {
        KEYDOWN = 0x0100,
        KEYUP = 0x0101,
        CHAR = 0x0102,
        IME_COMPOSITION = 0x010F,
        IME_ENDCOMPOSITION = 0x010E,
        IME_STARTCOMPOSITION = 0x010D,
    }
    public enum VK
    {
        CLEAR = 0x0C,
        RETURN = 0x0D,
        SHIFT = 0x10,
        CONTROL = 0x11,
        MENU = 0x12,
        CAPITAL = 0x14,
        CONVERT = 0x1C,
        PRIOR = 0x21,
        NEXT = 0x22,
        END = 0x23,
        HOME = 0x24,
        LEFT = 0x25,
        UP = 0x26,
        RIGHT = 0x27,
        DOWN = 0x28,
        INSERT = 0x2D,
        DELETE = 0x2E,
        LWIN = 0x5B,
        RWIN = 0x5C,
        NUMPAD0 = 0x60,
        NUMPAD1 = 0x61,
        NUMPAD2 = 0x62,
        NUMPAD3 = 0x63,
        NUMPAD4 = 0x64,
        NUMPAD5 = 0x65,
        NUMPAD6 = 0x66,
        NUMPAD7 = 0x67,
        NUMPAD8 = 0x68,
        NUMPAD9 = 0x69,
        MULTIPLY = 0x6A,
        ADD = 0x6B,
        SUBTRACT = 0x6D,
        DECIMAL = 0x6E,
        DIVIDE = 0x6F,
        NUMLOCK = 0x90,
        LSHIFT = 0xA0,
        RSHIFT = 0xA1,
        LCONTROL = 0xA2,
        RCONTROL = 0xA3,
        LMENU = 0xA4,
        RMENU = 0xA5,
    }
}
