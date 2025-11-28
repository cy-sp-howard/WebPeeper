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
        private static readonly Logger Logger = Logger.GetLogger<WebPeeperModule>();
        #region Service Managers
        internal SettingsManager SettingsManager => this.ModuleParameters.SettingsManager;
        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;
        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;
        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;
        #endregion
        public CefService CefService { get; private set; }
        public UIService UIService { get; private set; }
        public ModuleSettings Settings { get; private set; }
        public static BlishHud BlishHudInstance;
        public static MenuItem InstanceSettingsMenuItem;
        public static ModuleManager InstanceModuleManager;
        public static WebPeeperModule Instance;
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
            UIService = new UIService();
        }
        protected override async Task LoadAsync()
        {
            await Task.Run(() =>
            {
                CefService.Load();
                UIService.Load();
            });
        }
        protected override void Unload()
        {
            Settings?.Unload();
            CefService?.Unload();
            UIService?.Unload();
        }
    }

}
