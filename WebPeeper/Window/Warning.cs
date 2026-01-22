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
            _content = new() { Parent = this, };
            _alwaysHideCheckbox = new()
            {
                Text = Strings.UIService.Hide_Warning,
                Parent = this
            };
            _acceptBtn = new()
            {
                Text = Strings.UIService.Accept_Warning,
                Width = 100,
                Height = 30,
                Parent = this
            };
            _acceptBtn.Click += delegate { Accept(); };
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
            _content.Left = 15;
            _content.Height = ContentRegion.Height - _acceptBtn.Height - _alwaysHideCheckbox.Height - (int)OuterControlPadding.Y - 1;
            _content.Width = ContentRegion.Width - 30;
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
        readonly string _originText = Strings.UIService.Warning;
        string _text = "";
        BitmapFont _fontSize = Content.DefaultFont32;
        Rectangle _textRect = Rectangle.Empty;
        const int _gap = 10;
        protected override void OnResized(ResizedEventArgs e)
        {
            base.OnResized(e);
            _fontSize = Width > 500 ? Content.DefaultFont32 : Content.DefaultFont18;
            _text = CalculateText(_originText, ref _textRect, Rectangle.Empty);
            var minHeight = _textRect.Bottom + 30;
            if (Height < minHeight) { Height = minHeight; }
            else if (Height > minHeight)
            {
                var offsetY = Height / 3 - _textRect.Bottom / 2;
                if (offsetY <= 0) return;
                _textRect.Y += offsetY;
            }
        }
        public override void DoUpdate(GameTime gameTime)
        {
            base.DoUpdate(gameTime);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawStringOnCtrl(this, _text, _fontSize, _textRect, Color.Red);
        }
        string CalculateText(string text, ref Rectangle rect, Rectangle prevTextRect)
        {
            var edgeFix = _fontSize == Content.DefaultFont32 ? 80 : 0;
            rect.Width = Width;
            rect.Y = _gap + prevTextRect.Bottom;
            var result = DrawUtil.WrapText(_fontSize, text, rect.Width - edgeFix).Trim();
            var originSize = _fontSize.MeasureString(text);
            var size = _fontSize.MeasureString(result);
            rect.Height = (int)size.Height;
            if (originSize == size) { rect.X = Width / 2 - (int)(size.Width / 2); }
            else rect.X = 0;
            return result;
        }
    }
}
