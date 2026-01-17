using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Input;
using Blish_HUD.Settings;
using Blish_HUD.Settings.UI.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using MonoGame.Extended.TextureAtlases;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    public class ModuleSettings
    {
        const string _defaultSearchUrl = "https://www.google.com/search?q={text} site:wiki.guildwars2.com";
        const string _defaultHomeUrl = "https://wiki.guildwars2.com/";
        const string _defaultBgColor = "#00000000";
        public SettingEntry<KeyBinding> SettingsKey { get; private set; }
        public SettingEntry<KeyBinding> WebWindowKey { get; private set; }
        public SettingEntry<KeyBinding> ZoomInKey { get; private set; }
        public SettingEntry<KeyBinding> ZoomOutKey { get; private set; }
        public SettingEntry<KeyBinding> CaptureKeyboardKey { get; private set; }
        public SettingEntry<string> HomeUrl { get; private set; }
        public SettingEntry<string> SearchUrl { get; private set; }
        public SettingEntry<string> WebBgColor { get; private set; }
        public SettingEntry<float> WebWindowOpacity { get; private set; }
        public SettingEntry<bool> IsAutoPauseWeb { get; private set; }
        public SettingEntry<bool> IsAutoQuitProcess { get; private set; }
        public SettingEntry<bool> IsMobileLayout { get; private set; }
        public SettingEntry<bool> IsUseTouch { get; private set; }
        public SettingEntry<bool> IsCleanMode { get; private set; }
        public SettingEntry<bool> IsFollowBhFps { get; private set; }
        public SettingEntry<bool> IsBlockKeybinds { get; private set; }
        public ModuleSettings(SettingCollection settings)
        {
            InitUISetting(settings);
        }
        private void InitUISetting(SettingCollection settings)
        {
            SettingsKey = settings.DefineSetting(nameof(SettingsKey), new KeyBinding(ModifierKeys.Ctrl, Keys.F12), () => "Settings Toggle", () => "");
            SettingsKey.Value.Activated += ToggleSettings;
            SettingsKey.Value.Enabled = true;
            WebWindowKey = settings.DefineSetting(nameof(WebWindowKey), new KeyBinding(Keys.F12), () => "Web Window Toggle", () => "");
            WebWindowKey.Value.Activated += ToggleWebWindow;
            WebWindowKey.Value.Enabled = true;
            ZoomInKey = settings.DefineSetting(nameof(ZoomInKey), new KeyBinding(ModifierKeys.Ctrl, Keys.Add), () => "Zoom In", () => "Only works when the cursor is within the web area.");
            ZoomInKey.Value.Activated += ZoomInWeb;
            ZoomInKey.Value.Enabled = true;
            ZoomOutKey = settings.DefineSetting(nameof(ZoomOutKey), new KeyBinding(ModifierKeys.Ctrl, Keys.Subtract), () => "Zoom Out", () => "Only works when the cursor is within the web area.");
            ZoomOutKey.Value.Activated += ZoomOutWeb;
            ZoomOutKey.Value.Enabled = true;
            CaptureKeyboardKey = settings.DefineSetting(nameof(CaptureKeyboardKey), new KeyBinding(ModifierKeys.Ctrl, Keys.Space), () => "Focus the Blish-HUD Window", () => "For web input field, only works when the cursor is within the web area. In theory it would auto-focus when caret is flashing.");
            CaptureKeyboardKey.Value.Activated += FocusBHWindow;
            CaptureKeyboardKey.Value.Enabled = true;
            SearchUrl = settings.DefineSetting(nameof(SearchUrl), _defaultSearchUrl, () => "Search Engine", () => "{text} is represent text variable.");
            SearchUrl.SettingChanged += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.NewValue)) Task.Delay(10).ContinueWith(_ => SearchUrl.Value = _defaultSearchUrl);
            };
            HomeUrl = settings.DefineSetting(nameof(HomeUrl), _defaultHomeUrl, () => "Home Page", () => "");
            HomeUrl.SettingChanged += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.NewValue)) Task.Delay(10).ContinueWith(_ => HomeUrl.Value = _defaultHomeUrl);
            };
            WebBgColor = settings.DefineSetting(nameof(WebBgColor), _defaultBgColor, () => "Web Background      ", () => "Default is transparent.");
            WebBgColor.SetValidation((color) =>
            {
                try { ColorHelper.FromHex(color); return new(true); }
                catch { return new(false); }
            });
            WebBgColor.SettingChanged += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.NewValue)) Task.Delay(10).ContinueWith(_ => WebBgColor.Value = _defaultBgColor);
                WebPainter.Instance?.ApplyBgTexture();
            };
            WebWindowOpacity = settings.DefineSetting(nameof(WebWindowOpacity), 1f, () => $"Window Opacity < {Math.Round(WebWindowOpacity.Value, 2)} >", () => "");
            WebWindowOpacity.SetRange(0.1f, 1f);
            WebWindowOpacity.SettingChanged += (sender, e) =>
            {
                var uiService = WebPeeperModule.Instance.UIService;
                if (uiService is not null && uiService.BrowserWindow.Opacity > 0)
                {
                    uiService.BrowserWindow.Opacity = e.NewValue;
                }
                WebPeeperSettingsView.UpdateWebWindowOpacityTitle();
            };
            IsAutoPauseWeb = settings.DefineSetting(nameof(IsAutoPauseWeb), false, () => "Pause the Web Process while Close the Web Window", () => "");
            IsAutoQuitProcess = settings.DefineSetting(nameof(IsAutoQuitProcess), false, () => "Quit the Web Process while Close the Web Window", () => "");
            IsAutoQuitProcess.SettingChanged += (sender, e) =>
            {
                IsAutoPauseWeb.SetDisabled(e.NewValue);
                WebPeeperSettingsView.UpdateIsAutoPauseWebState();
            };
            IsAutoPauseWeb.SetDisabled(IsAutoQuitProcess.Value);
            IsMobileLayout = settings.DefineSetting(nameof(IsMobileLayout), true, () => "Use Mobile Website", () => "Whether use mobile User-Agent.");
            IsMobileLayout.SettingChanged += (s, e) => { WebPeeperModule.Instance.CefService.ApplyUserAgent(); };
            IsUseTouch = settings.DefineSetting(nameof(IsUseTouch), false, () => "Simulate Touch", () => "Left mouse button send touch event instead. It is useful for mobile websites.");
            IsCleanMode = settings.DefineSetting(nameof(IsCleanMode), false, () => "Auto Clean User-Data", () => "Clear cache and user-data while WebPeeper module initialize.");
            IsFollowBhFps = settings.DefineSetting(nameof(IsFollowBhFps), false, () => "Same as Blish-HUD FPS Setting", () => "Default is locked at 30 FPS, up to 60 FPS if unchecked.");
            IsBlockKeybinds = settings.DefineSetting(nameof(IsBlockKeybinds), true, () => "Block All Blish-HUD Keybinds while the Web is Accepting Input", () => "Uncheck if keybinds fail after typing.");
        }
        public void Unload()
        {
            SettingsKey.Value.Activated -= ToggleSettings;
            WebWindowKey.Value.Activated -= ToggleWebWindow;
            CaptureKeyboardKey.Value.Activated -= FocusBHWindow;
            ZoomInKey.Value.Activated -= ZoomInWeb;
            ZoomOutKey.Value.Activated -= ZoomOutWeb;
        }
        void ToggleSettings(object sender, EventArgs e)
        {
            WebPeeperModule.Instance.UIService?.ToggleSettings();
        }
        void ToggleWebWindow(object sender, EventArgs e)
        {
            WebPeeperModule.Instance.UIService?.ToggleBrowser();
        }
        void FocusBHWindow(object sender, EventArgs e)
        {
            if (WebPeeperModule.Instance.UIService?.BrowserWindow?.Visible == false) return;
            Utils.SetForegroundWindow(WebPeeperModule.BlishHudInstance.FormHandle);
            WebPeeperModule.Instance.CefService?.FocusBlurredElement();
        }
        void ZoomInWeb(object sender, EventArgs e)
        {
            if (WebPainter.Instance is null || !WebPainter.Instance.MouseOver) return;
            WebPainter.Instance.Zoom(1);
        }
        void ZoomOutWeb(object sender, EventArgs e)
        {
            if (WebPainter.Instance is null || !WebPainter.Instance.MouseOver) return;
            WebPainter.Instance.Zoom(-1);
        }
    }
    // SettingsView never call Unload, so cannot bind event (v1.2.0).
    public class WebPeeperSettingsView(SettingCollection settings) : View
    {
        static public Action UpdateWebWindowOpacityTitle;
        static public Action UpdateIsAutoPauseWebState;
        FlowPanel _rootflowPanel;
        readonly SettingCollection _settings = settings;
        protected override void Build(Container buildPanel)
        {
            _rootflowPanel = new FlowPanel()
            {
                Size = buildPanel.Size,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(5, 2),
                OuterControlPadding = new Vector2(10, 15),
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.AutoSize,
                AutoSizePadding = new Point(0, 15),
                Parent = buildPanel
            };

            foreach (var setting in _settings.Where(s => s.SessionDefined))
            {
                IView settingView = null;
                if (setting.EntryKey == "WebBgColor" && setting is SettingEntry<string> stringSetting)
                {
                    settingView = new HexColorSettingView(stringSetting, _rootflowPanel.Width);
                }
                if (settingView is not null || (settingView = SettingView.FromType(setting, _rootflowPanel.Width)) is not null)
                {
                    ViewContainer container = new()
                    {
                        WidthSizingMode = SizingMode.Fill,
                        HeightSizingMode = SizingMode.AutoSize,
                        Parent = _rootflowPanel
                    };
                    if (setting is SettingEntry<float> settingFloat && settingView is FloatSettingView settingViewFloat && settingFloat.EntryKey == "WebWindowOpacity")
                    {
                        UpdateWebWindowOpacityTitle = () =>
                        {
                            settingViewFloat.DisplayName = settingFloat.GetDisplayNameFunc();
                        };
                    }
                    else if (setting is SettingEntry<bool> settingBool && settingView is BoolSettingView settingViewBool && settingBool.EntryKey == "IsAutoPauseWeb")
                    {
                        UpdateIsAutoPauseWebState = () => { settingViewBool.Presenter.DoUpdateView(); };
                    }
                    container.Show(settingView); ;
                }
            }

            _rootflowPanel.ShowBorder = true;
            _rootflowPanel.CanCollapse = true;
        }
    }
    public class HexColorSettingView(SettingEntry<string> setting, int definedWidth = -1) : StringSettingView(setting, definedWidth)
    {
        ColorPreview _colorPreview;
        protected override void BuildSetting(Container buildPanel)
        {
            base.BuildSetting(buildPanel);
            var textBox = buildPanel.Children[1];
            textBox.Parent = null;
            _colorPreview = new ColorPreview() { Parent = buildPanel };
            ApplyColor();
            ValueChanged += delegate { ApplyColor(); };
            textBox.Parent = buildPanel;
            textBox.Moved += (s, e) => { _colorPreview.Left = e.CurrentLocation.X - _colorPreview.Width; };
        }
        void ApplyColor()
        {
            try
            {
                _colorPreview.Color = ColorHelper.FromHex(Value);
            }
            catch { }
        }
    }
    public class ColorPreview : Control
    {
        static readonly TextureRegion2D _colorTextureRegion = Blish_HUD.Controls.Resources.Control.TextureAtlasControl.GetRegion("colorpicker/cp-clr-v1");
        static readonly TextureRegion2D _borderTextureRegion = Blish_HUD.Controls.Resources.Control.TextureAtlasControl.GetRegion("colorpicker/cp-clr-active");
        public Color Color = Color.Transparent;
        public ColorPreview()
        {
            Size = new Size(28, 28);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawOnCtrl(this, _colorTextureRegion, bounds, Color);
            spriteBatch.DrawOnCtrl(this, _borderTextureRegion, bounds, Color.Black);
        }
    }
    public class Padding : Control
    {
        public string message = "";
        public Padding(int height = 16)
        {
            Size = new Point(0, height);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            Width = Parent.Width;
            if (message == "") return;
            spriteBatch.DrawStringOnCtrl(this, message, GameService.Content.DefaultFont14, new Rectangle(0, 0, Width, Height), Color.Red, false, false, 1, HorizontalAlignment.Center, VerticalAlignment.Middle);
        }
    }
}
