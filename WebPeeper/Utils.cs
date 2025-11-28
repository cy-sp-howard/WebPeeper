using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace BhModule.WebPeeper
{
    public static class Utils
    {

        [DllImport("imm32.dll")]
        internal static extern IntPtr ImmAssociateContextEx(IntPtr hWnd, int hIMC, int iace);
        [DllImport("imm32.dll")]
        internal static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")]
        internal static extern bool ImmSetOpenStatus(IntPtr hWnd, bool isOpen);
        [DllImport("imm32.dll")]
        internal static extern bool ImmSetCompositionWindow(IntPtr hIMC, CompositionForm pos);
        [DllImport("imm32.dll")]
        internal static extern bool ImmSetCandidateWindow(IntPtr hIMC, CompositionForm pos);
        [DllImport("Imm32.dll")]
        internal static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("Imm32.dll")]
        internal static extern int ImmGetCompositionStringW(IntPtr hIMC, int gcs, byte[] buffer, int bufferLen);
        [DllImport("Imm32.dll")]
        internal static extern bool ImmNotifyIME(IntPtr hIMC, uint action, uint index, uint value);
        [DllImport("user32.dll")]
        internal static extern int GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, UIntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        public static extern bool NativeSetForegroundWindow(IntPtr hWnd);
        public static bool SetForegroundWindow(IntPtr hWnd)
        {
            // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow#remarks
            // simulate user input event
            keybd_event(0, 0, 0, 0);
            return NativeSetForegroundWindow(hWnd);
        }
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        [DllImport("user32.dll")]
        public static extern short GetKeyState(VK nVirtKey);
        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        [DllImport("kernel32.dll")]
        public static extern bool SetDllDirectory(string lpPathName);
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);
        public static void SafeInvoke(this System.Windows.Forms.Form form, Action cb)
        {
            if (form.IsDisposed || !form.IsHandleCreated) return;
            form.Invoke(cb);
        }
        public static NotifyClass Notify = new();
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct CompositionForm
    {
        public uint Style;
        public int X;
        public int Y;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }


    public class NotifyClass : Control
    {
        private float duration = 3000;
        private string message;
        private bool waitingForPaint = true;
        private DateTime msgStartTime = DateTime.Now;
        public override void DoUpdate(GameTime gameTime)
        {
            Size = new Point(Parent.Size.X, 200);
            Location = new Point(0, Parent.Size.Y / 10 * 2);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (message != null)
            {
                if (waitingForPaint) msgStartTime = DateTime.Now;
                float existTime = (float)(DateTime.Now - msgStartTime).TotalMilliseconds;
                float remainTime = duration - (float)existTime;
                float opacity = remainTime > 1000 ? 1 : remainTime / 1000;
                if (opacity < 0)
                {
                    Clear();
                    return;
                }
                Color textColor = Color.Yellow * opacity;
                spriteBatch.DrawStringOnCtrl(this, message, GameService.Content.DefaultFont32, new Rectangle(0, 0, Width, Height), textColor, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            waitingForPaint = false;
        }
        public void Clear()
        {
            Parent = null;
            message = null;
        }
        public void Show(string text, float duration = 3000)
        {
            Parent = GameService.Graphics.SpriteScreen;
            msgStartTime = DateTime.Now;
            message = text;
            this.duration = duration;
        }
        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
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
    public enum GCS
    {
        COMPATTR = 0x10,
        COMPCLAUSE = 0x20,
        COMPREADATTR = 0x02,
        COMPREADCLAUSE = 0x04,
        COMPREADSTR = 0x01,
        COMPSTR = 0x08,
        CURSORPOS = 0x80,
        DELTASTART = 0x100,
        RESULTCLAUSE = 0x1000,
        RESULTREADCLAUSE = 0x400,
        RESULTREADSTR = 0x200,
        RESULTSTR = 0x800
    }
    public enum WS : uint
    {
        EX_TOPMOST = 0x00000008,
        EX_TRANSPARENT = 0x00000020,
        EX_TOOLWINDOW = 0x00000080,
        EX_CONTROLPARENT = 0x00010000,
        EX_APPWINDOW = 0x00040000,
        EX_LAYERED = 0x00080000
    }

}
