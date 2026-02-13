using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Runtime.InteropServices;

namespace BhModule.WebPeeper
{
    internal static class Utils
    {

        [DllImport("imm32.dll")]
        internal static extern bool ImmAssociateContextEx(IntPtr hWnd, int hIMC, int iace);
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
        internal static extern int SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, UIntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        public static extern bool NativeSetForegroundWindow(IntPtr hWnd);
        public static bool SetForegroundWindow(IntPtr hWnd)
        {
            // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow#remarks
            // simulate user input event
            keybd_event(0, 0, 0, 0);
            var resuilt = NativeSetForegroundWindow(hWnd);
            SetFocus(hWnd);
            return resuilt;
        }
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        [DllImport("kernel32.dll")]
        public static extern bool SetDllDirectory(string lpPathName);
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);
        public static bool IsForeground(this GameWindow window)
        {
            return GetForegroundWindow() == window.Handle;
        }
        public static void SafeInvoke(this System.Windows.Forms.Form form, Action cb)
        {
            if (form.IsDisposed || !form.IsHandleCreated) return;
            form.Invoke(cb);
        }
        public static readonly NotifyClass Notify = new();
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
    internal class CefPkgVersion(string cefSharpversion, string cefVersion = "")
    {
        readonly public Version CefSharp = new(cefSharpversion);
        readonly public Version Cef = new(string.IsNullOrEmpty(cefVersion) ? cefSharpversion : cefVersion);
        public override string ToString()
        {
            return CefSharp.ToString();
        }
        public override bool Equals(object obj)
        {
            if (obj is CefPkgVersion cefVerObj)
            {
                return cefVerObj == this;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return this.CefSharp.GetHashCode();
        }
        public static bool operator >(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp > v2.CefSharp;
        }
        public static bool operator <(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp < v2.CefSharp;
        }
        public static bool operator >=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp > v2.CefSharp;
        }
        public static bool operator <=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp < v2.CefSharp;
        }
        public static bool operator ==(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp == v2.CefSharp;
        }
        public static bool operator !=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp != v2.CefSharp;
        }
    }
    internal class NotifyClass : Control
    {
        float _duration = 3000;
        string _message;
        bool _waitingForPaint = true;
        DateTime _msgStartTime = DateTime.Now;
        public override void DoUpdate(GameTime gameTime)
        {
            Size = new Point(Parent.Size.X, 200);
            Location = new Point(0, Parent.Size.Y / 10 * 2);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_message != null)
            {
                if (_waitingForPaint) _msgStartTime = DateTime.Now;
                float existTime = (float)(DateTime.Now - _msgStartTime).TotalMilliseconds;
                float remainTime = _duration - (float)existTime;
                float opacity = remainTime > 1000 ? 1 : remainTime / 1000;
                if (opacity < 0)
                {
                    Clear();
                    return;
                }
                Color textColor = Color.Yellow * opacity;
                spriteBatch.DrawStringOnCtrl(this, _message, GameService.Content.DefaultFont32, new Rectangle(0, 0, Width, Height), textColor, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            _waitingForPaint = false;
        }
        public void Clear()
        {
            Parent = null;
            _message = null;
        }
        public void Show(string text, float duration = 3000)
        {
            Parent = GameService.Graphics.SpriteScreen;
            _msgStartTime = DateTime.Now;
            _message = text;
            _duration = duration;
        }
        protected override CaptureType CapturesInput()
        {
            return CaptureType.None;
        }
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
