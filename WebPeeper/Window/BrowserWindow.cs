using Blish_HUD;
using Blish_HUD.Common.UI.Views;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using CefSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    public class BrowserWindow : StandardWindow
    {
        static readonly Point _windowSize = new(500, 700);
        static readonly Point _windowBgOffset = new(-35, -30);
        static readonly Rectangle _windowBgEdgeSpliceBounds = new(35, 20, 60, 35);
        static readonly Rectangle _windowRegionParam = new(-_windowBgOffset.X, -_windowBgOffset.Y, _windowSize.X, _windowSize.Y);
        static readonly Rectangle _contentRegionParam = new(50, 20, _windowSize.X - 30, _windowSize.Y - 5);
        static readonly Texture2D _bg = BuildBg();
        Rectangle _titleBounds;
        static readonly Texture2D _pinTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("pin.png");
        Rectangle _pinBounds;
        Vector2 _pinOrigin;
        Rectangle _pinDestRect;
        Tooltip _pinTooltip;
        static readonly Color _pinColor = new(0xffffffff);
        static readonly Color _pinHoveredColor = new(0x00ffffff); // AABBGGRR
        bool _pinHovered = false;
        bool _firstShow = true;
        string _titleWithEllipsis = "-";
        CefService CefService => WebPeeperModule.Instance.CefService;
        ModuleSettings Settings => WebPeeperModule.Instance.Settings;
        public BrowserWindow() : base(_bg, _windowRegionParam, _contentRegionParam)
        {
            Subtitle = "-";
            Parent = Graphics.SpriteScreen;
            Title = null;
            SavesPosition = true;
            SavesSize = true;
            Location = new Point(Graphics.SpriteScreen.Width / 2, 100);
            CanResize = true;
            Id = "WebPeeper";
            _titleBounds = new Rectangle(40, -10, Width, 64);
            _pinBounds = new Rectangle(_titleBounds.Location.X - 30, _titleBounds.Location.Y + 20, 20, 20);
            _pinOrigin = new Vector2(_pinTexture.Width / 2, _pinTexture.Height / 2);
            _pinDestRect = new(new(_pinBounds.X + (int)_pinOrigin.X, _pinBounds.Y + (int)_pinOrigin.Y), _pinBounds.Size);
            SetMaxOpacity();
        }
        static Texture2D BuildBg()
        {
            var Graphics = GameService.Graphics;
            var Content = GameService.Content;

            var sourceTexture = Content.GetTexture("controls/window/502049");
            Color[] sourceData = new Color[sourceTexture.Width * sourceTexture.Height];
            sourceTexture.GetData<Color>(0, 0, sourceTexture.Bounds, sourceData, 0, sourceData.Length);

            var windowBgSize = new Point(_windowSize.X - _windowBgOffset.X + _windowBgEdgeSpliceBounds.Width, _windowSize.Y - _windowBgOffset.Y + 20 + _windowBgEdgeSpliceBounds.Height);
            Color[] windowBgData = new Color[windowBgSize.X * windowBgSize.Y];
            for (int row = 0; row < windowBgSize.Y; row++)
            {
                for (int col = 0; col < windowBgSize.X; col++)
                {
                    var overLocation = new Point(col - (windowBgSize.X - _windowBgEdgeSpliceBounds.Width), row - (windowBgSize.Y - _windowBgEdgeSpliceBounds.Height));
                    if (overLocation.X >= 0 || overLocation.Y >= 0)
                    {
                        if (col < _windowBgEdgeSpliceBounds.X || row < _windowBgEdgeSpliceBounds.Y) continue;
                        var overColFirstIndex = (sourceTexture.Height - windowBgSize.Y + row + 1) * sourceTexture.Width - 1 - _windowBgEdgeSpliceBounds.Width;
                        windowBgData[row * windowBgSize.X + col] = sourceData[overColFirstIndex + overLocation.X];
                    }
                    else
                    {
                        windowBgData[row * windowBgSize.X + col] = sourceData[row * sourceTexture.Width + col];
                    }
                }
            }
            using var ctx = Graphics.LendGraphicsDeviceContext();
            var windowBg = new Texture2D(ctx.GraphicsDevice, windowBgSize.X, windowBgSize.Y);
            windowBg.SetData(windowBgData);
            return windowBg;
        }
        void SetMaxOpacity()
        {
            // reflection
            var animFade = typeof(WindowBase2).GetField("_animFade", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this) as Glide.Tween;
            animFade.OnUpdate(() =>
            {
                if (Opacity > Settings.WebWindowOpacity.Value)
                {
                    Opacity = Settings.WebWindowOpacity.Value;
                }
            });
        }
        void PaintTitle(SpriteBatch spriteBatch)
        {
            try
            {
                // cjk not support
                spriteBatch.DrawStringOnCtrl(this, _titleWithEllipsis, Content.DefaultFont16, _titleBounds, ContentService.Colors.ColonialWhite);
            }
            finally { }
        }
        void PaintPin(SpriteBatch spriteBatch)
        {
            spriteBatch.DrawOnCtrl(
                this,
                _pinTexture,
                _pinDestRect,
                _pinTexture.Bounds,
                _pinHovered ? _pinHoveredColor : _pinColor,
                CanCloseWithEscape ? 0 : MathHelper.ToRadians(-45f),
                _pinOrigin);
        }
        public Task<bool> PrepareQuitBrowser()
        {
            if (!Visible)
            {
                ClearView();
                return Task.FromResult(true);
            }
            var tcs = new TaskCompletionSource<bool>();
            void onHidden(object sender, EventArgs e)
            {
                ClearView();
                tcs.SetResult(true);
            }
            Hidden += onHidden;
            Hide();
            tcs.Task.ContinueWith((_) =>
            {
                Hidden -= onHidden;
            });
            return tcs.Task;
        }
        protected override void OnShown(EventArgs e)
        {
            if (_firstShow)
            {
                _firstShow = false;
                var maxHeight = Parent.Height - 100;
                if (Height > maxHeight) Height = maxHeight;
                var maxWidth = Parent.Width - 200;
                if (Width > maxWidth) Width = maxWidth;
            }
            if (CefService.WebBrowser == null) return;
            if (Settings.IsAutoPauseWeb.Value)
            {
                CefService.WebBrowser?.GetBrowserHost().WasHidden(false);
            }
            base.OnShown(e);
        }
        protected override void OnHidden(EventArgs e)
        {
            if (CefService.WebBrowser is not null)
            {
                if (CefService.WebBrowser.CanExecuteJavascriptInMainFrame) CefService.WebBrowser.ExecuteScriptAsync("webPeeper_blur()");
                if (Settings.IsAutoQuitPrcess.Value) CefService.CloseWebBrowser();
                else if (Settings.IsAutoPauseWeb.Value)
                {
                    CefService.WebBrowser?.GetBrowserHost().WasHidden(true);
                }
            }
            BookmarkPanel.Instance?.SetChildrenEditState(false);
            base.OnHidden(e);
        }
        public override void RecalculateLayout()
        {
            _titleWithEllipsis = string.IsNullOrWhiteSpace(Subtitle) ? "-" : Subtitle;
            var textWidth = Content.DefaultFont16.MeasureString(_titleWithEllipsis).Width;
            var textOverWidth = textWidth - _titleBounds.Width + _titleBounds.X + 120;
            if (textOverWidth > 0)
            {
                var eachTextWidth = textWidth / _titleWithEllipsis.Length;
                var overTextChar = textOverWidth / eachTextWidth;
                var shrinkTextIndex = _titleWithEllipsis.Length - (int)overTextChar;
                if (shrinkTextIndex <= 0) return;
                _titleWithEllipsis = _titleWithEllipsis.Substring(0, shrinkTextIndex);
                _titleWithEllipsis += "...";
            }

            _pinBounds.Location = new(_titleBounds.Location.X - 30, _titleBounds.Location.Y + _pinBounds.Height);
            base.RecalculateLayout();
        }
        public override void PaintAfterChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            base.PaintAfterChildren(spriteBatch, bounds);
            PaintTitle(spriteBatch);
            PaintPin(spriteBatch);
        }
        protected override void OnMouseMoved(MouseEventArgs e)
        {
            _pinHovered = _pinBounds.Contains(RelativeMousePosition);
            if (_pinTooltip is null && _pinHovered)
            {
                _pinTooltip = new Tooltip(new BasicTooltipView("Click To " + (CanCloseWithEscape ? "Ignore" : "Allow") + " Esc To Close Window"));
                _pinTooltip.Show();
            }
            else if (!_pinHovered && _pinTooltip is not null)
            {
                _pinTooltip.Hide(); _pinTooltip = null;
            }
            base.OnMouseMoved(e);
        }
        protected override void OnLeftMouseButtonPressed(MouseEventArgs e)
        {
            if (_pinHovered) CanCloseWithEscape = !CanCloseWithEscape;
            base.OnLeftMouseButtonPressed(e);
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            _titleBounds.Width = Width;
            base.OnResized(e);
        }
        protected override Point HandleWindowResize(Point newSize)
        {
            return new Point(MathHelper.Clamp(newSize.X, 200, 1024),
                             MathHelper.Clamp(newSize.Y, 300, 1024));
        }
        protected override void DisposeControl()
        {
            WebPainter.DisposeWebTexture();
            _bg?.Dispose();
            base.DisposeControl();
        }
    }
}
