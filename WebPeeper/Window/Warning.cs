using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using System;

namespace BhModule.WebPeeper.Window
{
    public class Warning : FlowPanel
    {
        readonly WarningContent _content;
        readonly Checkbox _alwaysHideCheckbox;
        readonly StandardButton _acceptBtn;
        readonly Action _createWebPainter;
        static public bool Accepted = !WebPeeperModule.Instance.Settings.IsShowWarning.Value;
        public Warning(Action createWebPainter)
        {
            _createWebPainter = createWebPainter;
            FlowDirection = ControlFlowDirection.SingleTopToBottom;
            CanScroll = true;
            OuterControlPadding = new(0, 10);
            _acceptBtn = new()
            {
                Text = "Got It",
                Width = 100,
                Height = 30,
                Parent = this
            };
            _acceptBtn.Click += delegate { Accept(); };
            _alwaysHideCheckbox = new()
            {
                Text = "Don't show again.",
                Parent = this
            };
            _content = new() { Parent = this, };
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            base.OnResized(e);
            RecalculateLayout();
            WebPeeperModule.Instance.CefService.SetBrowserSize(Width, Height);
        }
        public override void RecalculateLayout()
        {
            base.RecalculateLayout();
            if (_content is null || _acceptBtn is null || _alwaysHideCheckbox is null) return;
            _content.Height = ContentRegion.Height - _acceptBtn.Height - _alwaysHideCheckbox.Height - (int)OuterControlPadding.Y - 1;
            _content.Width = ContentRegion.Width - 1;
            _acceptBtn.Left = ContentRegion.Width / 2 - _acceptBtn.Width / 2;
            _alwaysHideCheckbox.Left = ContentRegion.Width / 2 - _alwaysHideCheckbox.Width / 2;
        }
        void Accept()
        {
            Parent = null;
            Accepted = true;
            _createWebPainter();
            WebPeeperModule.Instance.Settings.IsShowWarning.Value = !_alwaysHideCheckbox.Checked;
            Dispose();
        }
    }
    public class WarningContent : Control
    {
        string _line1 = "You are using an OUTDATED Chromium Version.";
        string _line2 = "DO NOT Browse Untrusted or Security-Sensitive Websites.";
        string _line3 = "You could BE ATTACKED through known Vulnerabilities.";
        BitmapFont _fontSize = Content.DefaultFont32;
        Rectangle _line1Rect = Rectangle.Empty;
        Rectangle _line2Rect = Rectangle.Empty;
        Rectangle _line3Rect = Rectangle.Empty;
        const int _gap = 10;
        protected override void OnResized(ResizedEventArgs e)
        {
            base.OnResized(e);
            _fontSize = Width > 300 ? Content.DefaultFont32 : Content.DefaultFont18;
            SetLineRect(ref _line1, ref _line1Rect, Rectangle.Empty);
            SetLineRect(ref _line2, ref _line2Rect, _line1Rect);
            SetLineRect(ref _line3, ref _line3Rect, _line2Rect);
            if (Height < _line3Rect.Bottom) { Height = _line3Rect.Bottom; }
            else if (Height > _line3Rect.Bottom)
            {
                var offsetY = Height / 3 - _line3Rect.Bottom / 2;
                if (offsetY <= 0) return;
                _line1Rect.Y += offsetY;
                _line2Rect.Y += offsetY;
                _line3Rect.Y += offsetY;
            }
        }
        public override void DoUpdate(GameTime gameTime)
        {
            base.DoUpdate(gameTime);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawStringOnCtrl(this, _line1, _fontSize, _line1Rect, Color.Red);
            spriteBatch.DrawStringOnCtrl(this, _line2, _fontSize, _line2Rect, Color.Red);
            spriteBatch.DrawStringOnCtrl(this, _line3, _fontSize, _line3Rect, Color.Red);
        }
        void SetLineRect(ref string text, ref Rectangle rect, Rectangle prevTextRect)
        {
            rect.Width = Width;
            rect.Y = _gap + prevTextRect.Bottom;
            text = DrawUtil.WrapText(_fontSize, text.Replace("\n", ""), rect.Width - 20).Trim();
            var size = _fontSize.MeasureString(text);
            rect.Height = (int)size.Height;
            if (!text.Contains("\n")) { rect.X = Width / 2 - (int)(size.Width / 2); }
            else rect.X = 0;
        }
    }
}
