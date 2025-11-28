using Blish_HUD;
using Blish_HUD.Controls;
using System.Collections.Generic;

namespace BhModule.WebPeeper
{
    public class UIService
    {
        private CornerIcon _browserCornerIcon;
        private BrowserWindow _browserWindow;
        public BrowserWindow BrowserWindow { get => _browserWindow; }
        public void Load()
        {
            BuildBrowserWindow();
            BuildCornerIcon();
        }
        public void Unload()
        {
            _browserWindow.Dispose();
            _browserCornerIcon.Dispose();
        }
        public void ToggleBrowser()
        {
            _browserWindow.ToggleWindow(new BrowserWindowView());
        }
        public void ToggleSettings()
        {
            var menuItem = WebPeeperModule.InstanceSettingsMenuItem;
            if (GameService.Overlay.BlishHudWindow.Visible && menuItem.Selected)
            {
                GameService.Overlay.BlishHudWindow.Hide();
                return;
            }
            GameService.Overlay.BlishHudWindow.Show();
            menuItem.Select();
        }
        private void BuildBrowserWindow()
        {
            _browserWindow = new BrowserWindow();
        }
        private void BuildCornerIcon()
        {
            var Content = GameService.Content;
            _browserCornerIcon = new CornerIcon(
                WebPeeperModule.Instance.ContentsManager.GetTexture("logo.png"),
                WebPeeperModule.Instance.ContentsManager.GetTexture("logo-hover.png"),
                WebPeeperModule.InstanceModuleManager.Manifest.Name)
            {
                Priority = int.MaxValue,
                Parent = GameService.Graphics.SpriteScreen,
            };
            _browserCornerIcon.LeftMouseButtonReleased += delegate { ToggleBrowser(); };
            _browserCornerIcon.Menu = new ContextMenuStrip(GetContextMenuItems);
        }
        IEnumerable<ContextMenuStripItem> GetContextMenuItems()
        {
            var settings = new ContextMenuStripItem("Settings");
            settings.Click += delegate { ToggleSettings(); };
            yield return settings;

            var disposeBrowser = new ContextMenuStripItem("Quit Process");
            disposeBrowser.Click += delegate { WebPeeperModule.Instance.CefService.CloseWebBrowser(); };
            yield return disposeBrowser;

        }


    }
}
