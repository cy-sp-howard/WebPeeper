using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using CefHelper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Diagnostics;

namespace BhModule.WebPeeper
{
    internal class WebPainter : Control
    {
        public static WebPainter Instance { get; private set; }
        public Point LocationAtForm
        {
            get => Instance is null ? Point.Zero : new((int)(Instance.AbsoluteBounds.Location.X * GameService.Graphics.UIScaleMultiplier),
                    (int)(Instance.AbsoluteBounds.Location.Y * GameService.Graphics.UIScaleMultiplier));
        }
        static Texture2D _webTexture; // dispose when module unload
        byte[] _webTextureBufferBytes = [];
        Rectangle _webTextureRect = Rectangle.Empty;
        CefService CefService => WebPeeperModule.Instance.CefService;
        ModuleSettings Settings => WebPeeperModule.Instance.Settings;
        bool _isTriggerMouseLeftButtonPressed = false;
        bool _isTriggerMouseRightButtonPressed = false;
        bool _isMouseStateLeftButtonPressed = false;
        bool _isMouseStateRightButtonPressed = false;
        static bool _isError = false;
        static readonly Texture2D _errorTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("error.png");
        static readonly Texture2D _quesTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("what.png");
        static readonly Color _errorTextureColor = new(0x80808080);
        Rectangle _errorTextureRect = Rectangle.Empty;
        Rectangle _questionTextureRect = Rectangle.Empty;
        Process _cefPaintProcess;
        Color _webBackgroundColor;
        public bool Disabled = false;
        public WebPainter()
        {
            Instance = this;
            Browser.Paint += CefOnPaint;
            Browser.FrameLoadStart += OnFrameLoadStart;
            Browser.UrlLoadError += OnUrlLoadError;
            GameService.Input.Keyboard.KeyStateChanged += KeyboardHandler;
            ApplyBgTexture();
            Browser.Repaint();
        }
        public override Control TriggerMouseInput(MouseEventType mouseEventType, MouseState ms)
        {
            // only work when bh window not focused
            if (!Disabled) { MouseHandler(mouseEventType, ms); }
            return base.TriggerMouseInput(mouseEventType, ms);
        }
        protected override void OnMouseLeft(MouseEventArgs e)
        {
            if (_isTriggerMouseLeftButtonPressed)
            {
                MouseHandler(MouseEventType.LeftMouseButtonReleased, GameService.Input.Mouse.State);
            }
            if (_isTriggerMouseRightButtonPressed)
            {
                MouseHandler(MouseEventType.RightMouseButtonReleased, GameService.Input.Mouse.State);
            }
            base.OnMouseLeft(e);
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            _webTextureRect.Size = Size;
            Browser.SetSize(Width, Height);
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
                spriteBatch.DrawOnCtrl(this, _quesTexture, _questionTextureRect, _quesTexture.Bounds, _errorTextureColor, MathHelper.ToRadians(30f), Vector2.Zero);
            }
            else
            {
                spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, _webTextureRect, _webBackgroundColor);
                spriteBatch.DrawOnCtrl(this, _webTexture, _webTextureRect);
            }
        }
        public override void DoUpdate(GameTime gameTime)
        {
            if (!_mouseOver) return;
            var ms = GameService.Input.Mouse.State; // handle mouse button event when bh window focused
            var isLeftPressed = ms.LeftButton == ButtonState.Pressed;
            if (isLeftPressed != _isMouseStateLeftButtonPressed)
            {
                _isMouseStateLeftButtonPressed = isLeftPressed;
                if (isLeftPressed != _isTriggerMouseLeftButtonPressed) // _isTriggerMouseLeftButtonPressed trigger early
                {
                    MouseHandler(isLeftPressed ? MouseEventType.LeftMouseButtonPressed : MouseEventType.LeftMouseButtonReleased, ms);
                }
            }
            var isRightPressed = ms.RightButton == ButtonState.Pressed;
            if (isRightPressed != _isMouseStateRightButtonPressed)
            {
                _isMouseStateRightButtonPressed = isRightPressed;
                if (isRightPressed != _isTriggerMouseRightButtonPressed)
                {
                    MouseHandler(isRightPressed ? MouseEventType.RightMouseButtonPressed : MouseEventType.RightMouseButtonReleased, ms);
                }
            }
        }
        void OnFrameLoadStart()
        {
            SetErrorState(false);
        }
        public void OnUrlLoadError(string url)
        {
            SetErrorState(true);
        }
        bool CefOnPaint(IntPtr bufferHandle, int width, int height)
        {
            if (!WebPeeperModule.Instance.UiService.BrowserWindow.Visible) return true;
            var bufferSize = width * height * sizeof(int);
            if (bufferSize != _webTextureBufferBytes.Length)
            {
                _webTextureBufferBytes = new byte[bufferSize];
            }
            _cefPaintProcess ??= Process.GetCurrentProcess();
            Utils.ReadProcessMemory(_cefPaintProcess.Handle, bufferHandle, _webTextureBufferBytes, bufferSize, out _);
            if (_webTexture is null || _webTexture.Format != SurfaceFormat.Bgra32 || _webTexture.Width != width || _webTexture.Height != height)
            {
                DisposeWebTexture();
                using var ctx = Graphics.LendGraphicsDeviceContext();
                _webTexture = new Texture2D(
                         ctx.GraphicsDevice,
                         width,
                         height,
                         false,
                         SurfaceFormat.Bgra32
                         );
            }
            _webTexture.SetData(_webTextureBufferBytes);
            return true;
        }
        public void SetErrorState(bool state)
        {
            _isError = state;
        }
        public void ApplyBgTexture()
        {
            try { _webBackgroundColor = ColorHelper.FromHex(Settings.WebBgColor.Value); }
            catch { }
        }
        void SetErrorTextureRect()
        {
            var errTextureSize = MathHelper.Min((int)(Size.X * 0.6), (int)(Size.Y * 0.6));
            _errorTextureRect = new(0, Size.Y - errTextureSize, errTextureSize, errTextureSize);
            var quesTextureSize = (int)(errTextureSize * 0.2);
            _questionTextureRect = new((int)(errTextureSize * 0.7), Size.Y - (int)(errTextureSize * 1.05), quesTextureSize, quesTextureSize);
        }
        void MouseHandler(MouseEventType mouseEventType, MouseState ms)
        {
            var ctrlPos = ms.Position - AbsoluteBounds.Location;
            var mouseEvtFlag = GetCurrentKeyboardModifiers();
            if (_isTriggerMouseLeftButtonPressed) mouseEvtFlag |= CefEvtModifiresFlags.LeftMouseButton;
            else if (_isTriggerMouseRightButtonPressed) mouseEvtFlag |= CefEvtModifiresFlags.RightMouseButton;

            if (mouseEventType == MouseEventType.LeftMouseButtonPressed)
            {
                _isTriggerMouseLeftButtonPressed = true;
            }
            else if (mouseEventType == MouseEventType.RightMouseButtonPressed)
            {
                _isTriggerMouseRightButtonPressed = true;
            }
            else if (mouseEventType == MouseEventType.LeftMouseButtonReleased)
            {
                _isTriggerMouseLeftButtonPressed = false;
            }
            else if (mouseEventType == MouseEventType.RightMouseButtonReleased)
            {
                _isTriggerMouseRightButtonPressed = false;
            }

            Browser.SendCursorEvent(ctrlPos.X, ctrlPos.Y, ms.ScrollWheelValue, mouseEventType, (int)mouseEvtFlag, Settings.IsUseTouch.Value);
        }
        void KeyboardHandler(object sender, KeyboardEventArgs e)
        {
            if (!_mouseOver || WebPeeperModule.BlishHudInstance.Window.IsForeground()) return;
            var isKeyDown = e.EventType == KeyboardEventType.KeyDown;
            var key = e.Key switch
            {
                Keys.LeftAlt or Keys.RightAlt => (int)VK.MENU,
                Keys.LeftControl or Keys.RightControl => (int)VK.CONTROL,
                Keys.LeftShift or Keys.RightShift => (int)VK.SHIFT,
                _ => (int)e.Key,
            };
            Browser.SendKeyEvent((int)GetCurrentKeyboardModifiers(), isKeyDown, key);
        }
        protected override void DisposeControl()
        {
            GameService.Input.Keyboard.KeyStateChanged -= KeyboardHandler;
            Browser.Paint -= CefOnPaint;
            Browser.FrameLoadStart -= OnFrameLoadStart;
            Browser.UrlLoadError -= OnUrlLoadError;
            _cefPaintProcess?.Dispose();
        }
        static public void DisposeWebTexture() { _webTexture?.Dispose(); _webTexture = null; }
        static CefEvtModifiresFlags GetCurrentKeyboardModifiers()
        {
            var cefModifiers = CefEvtModifiresFlags.None;
            var monoModifiers = GameService.Input.Keyboard.ActiveModifiers;
            if (monoModifiers.HasFlag(ModifierKeys.Ctrl))
            {
                cefModifiers |= CefEvtModifiresFlags.ControlDown;
            }
            if (monoModifiers.HasFlag(ModifierKeys.Alt))
            {
                cefModifiers |= CefEvtModifiresFlags.AltDown;
            }
            if (monoModifiers.HasFlag(ModifierKeys.Shift))
            {
                cefModifiers |= CefEvtModifiresFlags.ShiftDown;
            }
            return cefModifiers;
        }
        enum CefEvtModifiresFlags : uint
        {
            None = 0u,
            CapsLockOn = 1u,
            ShiftDown = 2u,
            ControlDown = 4u,
            AltDown = 8u,
            LeftMouseButton = 0x10u,
            MiddleMouseButton = 0x20u,
            RightMouseButton = 0x40u,
        }
    }
}
