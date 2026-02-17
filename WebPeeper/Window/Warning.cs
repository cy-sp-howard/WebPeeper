using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace BhModule.WebPeeper.Window
{
    internal class Warning : FlowPanel
    {
        static readonly IReadOnlyDictionary<CefAvailableVersion, string> _versions = GetVersions();
        readonly Dropdown _versionSeletor;
        readonly WarningContent _content;
        readonly Checkbox _alwaysHideCheckbox;
        readonly StandardButton _acceptBtn;
        public event EventHandler<EventArgs> Accepted;
        static public bool IsAccepted { get; private set; } = !WebPeeperModule.Instance.Settings.IsShowWarning.Value;
        public Warning()
        {
            FlowDirection = ControlFlowDirection.SingleTopToBottom;
            CanScroll = true;
            _versionSeletor = new Dropdown()
            {
                Parent = this,
                Width = 110,
                SelectedItem = _versions[WebPeeperModule.Instance.Settings.CefVersion.Value],
                BasicTooltipText = WebPeeperModule.Instance.Settings.CefVersion.Description,
            };
            _versionSeletor.ValueChanged += (s, e) =>
            {
                var version = _versions.FirstOrDefault(v => v.Value == e.CurrentValue);
                WebPeeperModule.Instance.Settings.CefVersion.Value = version.Key;
            };
            foreach (var item in _versions)
            {
                _versionSeletor.Items.Add(item.Value);
            }
            WebPeeperModule.Instance.Settings.CefVersion.SettingChanged += UpdateVersion;
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
        }
        public override void RecalculateLayout()
        {
            base.RecalculateLayout();
            if (_versionSeletor is null || _content is null || _acceptBtn is null || _alwaysHideCheckbox is null) return;
            _versionSeletor.Left = ContentRegion.Width / 2 - _versionSeletor.Width / 2;
            _content.Left = 15;
            _content.Height = ContentRegion.Height - _versionSeletor.Height - _acceptBtn.Height - _alwaysHideCheckbox.Height - (int)OuterControlPadding.Y - 1;
            _content.Width = ContentRegion.Width - 30;
            _alwaysHideCheckbox.Left = ContentRegion.Width / 2 - _alwaysHideCheckbox.Width / 2;
            _acceptBtn.Left = ContentRegion.Width / 2 - _acceptBtn.Width / 2;
        }
        void Accept()
        {
            IsAccepted = true;
            Accepted?.Invoke(this, EventArgs.Empty);
            WebPeeperModule.Instance.Settings.IsShowWarning.Value = !_alwaysHideCheckbox.Checked;
            Dispose();
        }
        void UpdateVersion(object sender, ValueChangedEventArgs<CefAvailableVersion> evt)
        {
            _versionSeletor.SelectedItem = _versions[evt.NewValue];
        }
        static Dictionary<CefAvailableVersion, string> GetVersions()
        {
            Dictionary<CefAvailableVersion, string> versions = [];
            var fields = typeof(CefAvailableVersion).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            foreach (var field in fields)
            {
                var descriptionAttr = (DescriptionAttribute)field.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault();
                versions.Add((CefAvailableVersion)field.GetValue(null), descriptionAttr.Description);
            }
            return versions;
        }
        protected override void DisposeControl()
        {
            Accepted = null;
            WebPeeperModule.Instance.Settings.CefVersion.SettingChanged -= UpdateVersion;
            base.DisposeControl();
        }
    }
    internal class WarningContent : Control
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
