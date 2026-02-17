using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    [Export(typeof(Blish_HUD.Modules.Module))]
    public class WebPeeperModule : Blish_HUD.Modules.Module
    {
        internal static readonly Logger Logger = Logger.GetLogger<WebPeeperModule>();
        #region Service Managers
        internal SettingsManager SettingsManager => ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => ModuleParameters.Gw2ApiManager;
        internal string DataFolder => DirectoriesManager.GetFullDirectoryPath(Name.ToLower());
        #endregion
        internal CefService CefService { get; private set; }
        internal ImeService ImeService { get; private set; }
        internal UiService UiService { get; private set; }
        internal DownloadService DownloadService { get; private set; }
        internal ModuleSettings Settings { get; private set; }
        internal static BlishHud BlishHudInstance { get; private set; }
        internal static MenuItem InstanceSettingsMenuItem { get; private set; }
        internal static ModuleManager InstanceModuleManager { get; private set; }
        internal static WebPeeperModule Instance { get; private set; }
        [ImportingConstructor]
        public WebPeeperModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
            // reflection
            var mainInstance = typeof(BlishHud).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            BlishHudInstance = mainInstance.GetValue(mainInstance.ReflectedType) as BlishHud;

            Instance = this;
        }
        protected override void DefineSettings(SettingCollection settings)
        {
            Settings = new ModuleSettings(settings);
        }
        public override IView GetSettingsView()
        {
            return new WebPeeperSettingsView(SettingsManager.ModuleSettings);
        }
        protected override void Initialize()
        {
            InstanceModuleManager = GameService.Module.Modules.FirstOrDefault(m => m.ModuleInstance == this);
            InstanceSettingsMenuItem = GameService.Overlay.SettingsTab.GetSettingMenus().Last()
                .Children.FirstOrDefault(i =>
                {
                    return ((MenuItem)i).Text == InstanceModuleManager.Manifest.Name;
                }) as MenuItem;

            CefService = new CefService();
            ImeService = new ImeService();
            UiService = new UiService();
            DownloadService = new DownloadService();
        }
        protected override async Task LoadAsync()
        {
            await Task.Run(() =>
            {
                Settings.Load();
                CefService.Load();
                UiService.Load();
            });
        }
        protected override void Unload()
        {
            Settings?.Unload();
            CefService?.Unload();
            ImeService?.Unload();
            UiService?.Unload();
        }
    }

}
