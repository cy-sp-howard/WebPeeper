using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Diagnostics;

namespace BhModule.WebPeeper
{
    public class WebPainter : Control
    {
        public static WebPainter Instance;
        public Point LocationAtForm
        {
            get => Instance is null ? Point.Zero : new((int)(Instance.AbsoluteBounds.Location.X * GameService.Graphics.UIScaleMultiplier),
                    (int)(Instance.AbsoluteBounds.Location.Y * GameService.Graphics.UIScaleMultiplier));
        }
        static Texture2D _webTexture;
        byte[] _webTextureBufferBytes = [];
        Rectangle _webTextureRect;
        CefService CefService => WebPeeperModule.Instance.CefService;
        ModuleSettings Settings => WebPeeperModule.Instance.Settings;
        ChromiumWebBrowser WebBrowser => CefService.WebBrowser;
        bool _isLeftMouseButtonPressed;
        bool _isRightMouseButtonPressed;
        static bool _isError;
        static readonly Texture2D _errorTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("error.png");
        static readonly Texture2D _quesTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("what.png");
        static readonly Color _errorTextureColor = new(0x80808080);
        Rectangle _errorTextureRect;
        Rectangle _quesTextureRect;
        Process _cefPaintProcess;
        Color _webBackgroundColor;
        public bool Disabled = false;
        public WebPainter()
        {
            Instance = this;
            WebBrowser.Paint += CefOnPaint;
            GameService.Input.Keyboard.KeyStateChanged += KeyboardHandler;
            ApplyBgTexture();
        }
        public override Control TriggerMouseInput(MouseEventType mouseEventType, MouseState ms)
        {
            if (WebBrowser != null && WebBrowser.IsBrowserInitialized && !Disabled)
            {
                var isUseTouch = Settings.IsUseTouch.Value;
                var borwserHost = WebBrowser.GetBrowserHost();
                var ctrlPos = ms.Position - AbsoluteBounds.Location;
                CefEventFlags mouseEvtFlag = GetCurrentKeyboardModifiers();
                if (_isLeftMouseButtonPressed) mouseEvtFlag |= CefEventFlags.LeftMouseButton;
                else if (_isRightMouseButtonPressed) mouseEvtFlag |= CefEventFlags.RightMouseButton;

                if (mouseEventType == MouseEventType.MouseMoved)
                {
                    if (isUseTouch && _isLeftMouseButtonPressed)
                    {
                        CefSharp.Structs.TouchEvent touchEvt = new()
                        {
                            Id = 1,
                            X = (float)ctrlPos.X,
                            Y = (float)ctrlPos.Y,
                            PointerType = PointerType.Touch,
                            Pressure = 1,
                            Type = TouchEventType.Moved,
                            Modifiers = mouseEvtFlag,
                        };
                        borwserHost.SendTouchEvent(touchEvt);
                    }
                    else
                    {
                        MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                        borwserHost.SendMouseMoveEvent(mouseEvt, false);
                    }
                }
                else if (mouseEventType == MouseEventType.MouseWheelScrolled)
                {
                    MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                    var wheelValue = ms.ScrollWheelValue < -1000 ? 240 : ms.ScrollWheelValue;
                    if (mouseEvtFlag.HasFlag(CefEventFlags.ControlDown) && !mouseEvtFlag.HasFlag(CefEventFlags.AltDown) && !mouseEvtFlag.HasFlag(CefEventFlags.ShiftDown))
                    {
                        Zoom(wheelValue > 0 ? 1 : -1);
                    }
                    else
                    {
                        borwserHost.SendMouseWheelEvent(mouseEvt, 0, wheelValue);
                    }
                }
                else if (mouseEventType == MouseEventType.LeftMouseButtonPressed)
                {
                    borwserHost.SendFocusEvent(true);
                    if (isUseTouch)
                    {
                        CefSharp.Structs.TouchEvent touchEvt = new()
                        {
                            Id = 1,
                            X = (float)ctrlPos.X,
                            Y = (float)ctrlPos.Y,
                            PointerType = PointerType.Touch,
                            Pressure = 1,
                            Type = TouchEventType.Pressed,
                            Modifiers = mouseEvtFlag,
                        };
                        borwserHost.SendTouchEvent(touchEvt);
                    }
                    else
                    {
                        MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                        borwserHost.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, false, 0);
                    }
                    _isLeftMouseButtonPressed = true;
                }
                else if (mouseEventType == MouseEventType.RightMouseButtonPressed)
                {
                    borwserHost.SendFocusEvent(true);
                    MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                    borwserHost.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, false, 0);
                    _isRightMouseButtonPressed = true;
                }
                else if (mouseEventType == MouseEventType.LeftMouseButtonReleased)
                {
                    if (isUseTouch)
                    {
                        CefSharp.Structs.TouchEvent touchEvt = new()
                        {
                            Id = 1,
                            X = (float)ctrlPos.X,
                            Y = (float)ctrlPos.Y,
                            PointerType = PointerType.Touch,
                            Pressure = 1,
                            Type = TouchEventType.Released,
                            Modifiers = mouseEvtFlag,
                        };
                        borwserHost.SendTouchEvent(touchEvt);
                    }
                    else
                    {
                        MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                        borwserHost.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, _isLeftMouseButtonPressed, 0);
                    }
                    _isLeftMouseButtonPressed = false;
                }
                else if (mouseEventType == MouseEventType.RightMouseButtonReleased)
                {
                    MouseEvent mouseEvt = new(ctrlPos.X, ctrlPos.Y, mouseEvtFlag);
                    borwserHost.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, _isRightMouseButtonPressed, 0);
                    _isRightMouseButtonPressed = false;
                }
            }
            return base.TriggerMouseInput(mouseEventType, ms);
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            _webTextureRect = new(Point.Zero, Size);
            SetBrowserSize();
            SetErrorTextureRect();
            base.OnResized(e);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_webTexture is null)
            {
                LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, new Rectangle(Size.X / 2 - 50, Size.Y / 2 - 50, 100, 100));
            }
            else if (_isError)
            {
                spriteBatch.DrawOnCtrl(this, _errorTexture, _errorTextureRect, _errorTextureColor);
                spriteBatch.DrawOnCtrl(this, _quesTexture, _quesTextureRect, _quesTexture.Bounds, _errorTextureColor, MathHelper.ToRadians(30f), Vector2.Zero);
            }
            else
            {
                spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, _webTextureRect, _webBackgroundColor);
                spriteBatch.DrawOnCtrl(this, _webTexture, _webTextureRect);
            }
        }
        void CefOnPaint(object sender, OnPaintEventArgs e)
        {
            if (!WebPeeperModule.Instance.UIService.BrowserWindow.Visible) return;
            var bufferSize = e.Width * e.Height * sizeof(int);
            if (bufferSize != _webTextureBufferBytes.Length)
            {
                _webTextureBufferBytes = new byte[bufferSize];
            }
            _cefPaintProcess ??= Process.GetCurrentProcess();
            Utils.ReadProcessMemory(_cefPaintProcess.Handle, e.BufferHandle, _webTextureBufferBytes, bufferSize, out _);
            if (_webTexture is null || _webTexture.Format != SurfaceFormat.Bgra32 || _webTexture.Width != e.Width || _webTexture.Height != e.Height)
            {
                _webTexture?.Dispose();
                using var ctx = Graphics.LendGraphicsDeviceContext();
                _webTexture = new Texture2D(
                         ctx.GraphicsDevice,
                         e.Width,
                         e.Height,
                         false,
                         SurfaceFormat.Bgra32
                         );
            }
            _webTexture.SetData(_webTextureBufferBytes);
        }
        public void SetErrorState(bool state)
        {
            _isError = state;
        }
        public void ApplyBgTexture()
        {
            try { _webBackgroundColor = ColorHelper.FromHex(Settings.WebBgColor.Value); }
            finally { }
        }
        void SetErrorTextureRect()
        {
            var errTextureSize = MathHelper.Min((int)(Size.X * 0.6), (int)(Size.Y * 0.6));
            _errorTextureRect = new(0, Size.Y - errTextureSize, errTextureSize, errTextureSize);
            var quesTextureSize = (int)(errTextureSize * 0.2);
            _quesTextureRect = new((int)(errTextureSize * 0.7), Size.Y - (int)(errTextureSize * 1.05), quesTextureSize, quesTextureSize);
        }
        void SetBrowserSize()
        {
            WebBrowser.ResizeAsync(Width, Height);
        }
        void KeyboardHandler(object sender, KeyboardEventArgs e)
        {
            if (WebBrowser is null || !WebBrowser.IsBrowserInitialized || !_mouseOver || WebPeeperModule.BlishHudInstance.Form.Focused) return;
            var keyEvt = new KeyEvent
            {
                Modifiers = GetCurrentKeyboardModifiers(),
                Type = e.EventType == KeyboardEventType.KeyDown ? KeyEventType.KeyDown : KeyEventType.KeyUp,
                WindowsKeyCode = e.Key switch
                {
                    Keys.LeftAlt or Keys.RightAlt => (int)VK.MENU,
                    Keys.LeftControl or Keys.RightControl => (int)VK.CONTROL,
                    Keys.LeftShift or Keys.RightShift => (int)VK.SHIFT,
                    _ => (int)e.Key,
                }
            };
            WebBrowser.GetBrowserHost().SendKeyEvent(keyEvt);
        }
        public void Zoom(float rate)
        {
            if (WebBrowser is null) return;
            var getLevelTask = WebBrowser.GetZoomLevelAsync();
            getLevelTask.Wait(TimeSpan.FromSeconds(1));
            if (getLevelTask.IsCanceled) return;
            WebBrowser.SetZoomLevel(getLevelTask.Result + 0.2f * rate);
        }
        protected override void DisposeControl()
        {
            GameService.Input.Keyboard.KeyStateChanged -= KeyboardHandler;
            WebBrowser.Paint -= CefOnPaint;
            _cefPaintProcess?.Dispose();
        }
        static public void DisposeWebTexture() { _webTexture?.Dispose(); }
        static CefEventFlags GetCurrentKeyboardModifiers()
        {
            var cefModifiers = CefEventFlags.None;
            var monoModifiers = GameService.Input.Keyboard.ActiveModifiers;
            if (monoModifiers.HasFlag(ModifierKeys.Ctrl))
            {
                cefModifiers |= CefEventFlags.ControlDown;
            }
            if (monoModifiers.HasFlag(ModifierKeys.Alt))
            {
                cefModifiers |= CefEventFlags.AltDown;
            }
            if (monoModifiers.HasFlag(ModifierKeys.Shift))
            {
                cefModifiers |= CefEventFlags.ShiftDown;
            }
            return cefModifiers;
        }
    }
}
