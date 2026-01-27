using Blish_HUD;
using Blish_HUD.Graphics;
using CefHelper;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    public class CefService
    {
        public string LastAddressInputText = "";
        static public string CefSharpDllPath = DirectoryUtil.RegisterDirectory(DirectoryUtil.CachePath, "cefsharp/");
        static public string CefSettingFolder = DirectoryUtil.RegisterDirectory(WebPeeperModule.InstanceModuleManager.Manifest.Name.Replace(" ", "").ToLower());
        const string _mobileUserAgent = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Mobile Safari/537.36";
        const string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Safari/537.36";
        string _cefLocalesPath;
        public CefService()
        {
            SetupCefDllPath();
            SetupCefSharpDllFolder();
            WebPeeperModule.BlishHudInstance.Exiting += OnBlishHudExiting;
            Browser.BlishHudSchemeRequested += OnBlishHudSchemeRequested;
            Browser.FocusedChanged += OnFocusedChanged;
            Browser.TitleChanged += OnTitleChanged;
            Browser.FrameLoadStart += OnFrameLoadStart;
            Browser.UrlLoadError += OnUrlLoadError;
            Browser.FullscreenModeChanged += OnFullscreenModeChange;
        }
        public void Load()
        {
            LoadContextCreatedScript();
        }
        public void Unload()
        {
            WebPeeperModule.BlishHudInstance.Exiting -= OnBlishHudExiting;
            AppDomain.CurrentDomain.AssemblyResolve -= CefSharpCoreRuntimeResolver;
            Browser.Dispose();
        }
        void SetupCefDllPath()
        {
            void setLibCefDllFolder(object s, EventArgs e)
            {
                GameService.GameIntegration.Gw2Instance.Gw2Started -= setLibCefDllFolder;
#if DEBUG
                string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string nugetFolder = Path.Combine(userFolder, ".nuget", "packages");
                string cefFolder = Path.Combine(nugetFolder, "cef.redist.x64\\103.0.9\\CEF");
#else
                var gw2Folder = Path.GetDirectoryName(GameService.GameIntegration.Gw2Instance.Gw2Process.MainModule.FileName);
                var cefFolder = Path.Combine(gw2Folder, "bin64\\cef");
                _cefLocalesPath = cefFolder;
#endif
                //Utils.SetDllDirectory(cefFolder); // not working in Wine
                Environment.SetEnvironmentVariable("PATH", $"{cefFolder};{Environment.GetEnvironmentVariable("PATH")}");
            }

            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) setLibCefDllFolder(this, EventArgs.Empty);
            else GameService.GameIntegration.Gw2Instance.Gw2Started += setLibCefDllFolder;
        }
        void LoadContextCreatedScript()
        {
            using var fileStream = WebPeeperModule.Instance.ContentsManager.GetFileStream("onContextCreated.js") as MemoryStream;
            using TextReader reader = new StreamReader(fileStream, Encoding.UTF8);
            Browser.ContextCreatedScript = reader.ReadToEnd();
        }
        void ExtractFiles(string[] paths)
        {
            foreach (var path in paths)
            {
                var detinationFile = Path.Combine(CefSharpDllPath, path);
                Directory.CreateDirectory(Path.GetDirectoryName(detinationFile));
                byte[] file = WebPeeperModule.InstanceModuleManager.DataReader.GetFileBytes(path);
                try
                {
                    using var fileStream = new FileStream(detinationFile, FileMode.Create, FileAccess.Write, FileShare.Write, 4096);
                    fileStream.Write(file, 0, file.Length);
                }
                catch { }
            }
        }
        void SetupCefSharpDllFolder()
        {
            // because security policy cannot load "CefSharp.Core.Runtime.dll" from bytes[].
            AppDomain.CurrentDomain.AssemblyResolve += CefSharpCoreRuntimeResolver;
            // load CefSharp.dll by self, prevent system load twice. 
            Assembly.Load(WebPeeperModule.InstanceModuleManager.DataReader.GetFileBytes("CefSharp.dll"), []);

            ExtractFiles([
                "CefSharp.BrowserSubprocess.Core.dll",
                "CefSharp.BrowserSubprocess.exe",
                "CefSharp.dll",
                "_\\CefSharp.Core.Runtime.dll"
                ]);
        }
        private Assembly CefSharpCoreRuntimeResolver(object sender, ResolveEventArgs args)
        {
            var target = "CefSharp.Core.Runtime";
            if (args.Name.Contains(target))
            {
                // load at isolated folder, prevent load CefSharp.dll that is for "CefSharp.BrowserSubprocess.exe"
                return Assembly.LoadFrom(Path.Combine(CefSharpDllPath, $"_\\{target}.dll"));
            }
            return null;
        }
        public Task<Texture2D> GetScreenshot()
        {
            return Browser.GetScreenshot().ContinueWith(t =>
            {
                var bufferSize = t.Result.Length;
                using var ctx = GraphicsService.Graphics.LendGraphicsDeviceContext();
                using var memoryStream = new MemoryStream();
                memoryStream.Write(t.Result, 0, bufferSize);
                return Texture2D.FromStream(ctx.GraphicsDevice, memoryStream);
            });
        }
        async public void CloseWebBrowser()
        {
            await WebPeeperModule.Instance.UiService.BrowserWindow.PrepareQuitBrowser();
            Browser.Close();
        }
        public void ApplyUserAgent()
        {
            var agentString = WebPeeperModule.Instance.Settings.IsMobileLayout.Value ? _mobileUserAgent : _defaultUserAgent;
            Browser.ApplyUserAgent(agentString);
        }
        void OnBlishHudExiting(object sender, EventArgs e)
        {
            if (Browser.WebBrowser is null) return;
            Browser.Dispose(); // make sure close for restart\
        }
        Stream OnBlishHudSchemeRequested(string filePath)
        {
            return WebPeeperModule.Instance.ContentsManager.GetFileStream(filePath); // cef auto dispose stream
        }
        void OnFocusedChanged(bool focused)
        {
            WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
            {
                if (focused) WebPeeperModule.Instance.ImeService.Enable();
                else WebPeeperModule.Instance.ImeService.Disable();
            });
        }
        void OnTitleChanged(string title)
        {
            var uiService = WebPeeperModule.Instance.UiService;
            if (uiService is null || uiService.BrowserWindow is null) return;
            uiService.BrowserWindow.Subtitle = title;
        }
        void OnFrameLoadStart()
        {
            WebPainter.Instance?.SetErrorState(false);
        }
        public void OnUrlLoadError(string failedUrl)
        {
            NavigationBar.Instance?.SetAddressInputText(failedUrl);
            var text = LastAddressInputText;
            LastAddressInputText = "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (Uri.TryCreate(text, UriKind.Absolute, out Uri _))
                {
                    WebPainter.Instance?.SetErrorState(true);
                }
                else
                {
                    Browser.LoadUrlAsync(new Regex("{\\s*text\\s*}").Replace(WebPeeperModule.Instance.Settings.SearchUrl.Value, Uri.EscapeDataString(text)));
                }
            }
        }
        void OnFullscreenModeChange(bool isFullscreen)
        {
            if (isFullscreen) NavigationBar.Instance?.Hide();
            else NavigationBar.Instance?.Show();
        }
        public Task<bool> CreateWebBrowser()
        {
            var moduleSettings = WebPeeperModule.Instance.Settings;
            Browser.CefSettingInit(
                moduleSettings.IsMobileLayout.Value ? _mobileUserAgent : _defaultUserAgent,
                _cefLocalesPath,
                CefSettingFolder,
                CefSharpDllPath,
                WebPeeperModule.Instance.Settings.IsCleanMode.Value
                );
            var frameRate = 30;
            if (WebPeeperModule.Instance.Settings.IsFollowBhFps.Value)
            {
                frameRate = GameService.Graphics.FrameLimiter switch
                {
                    FramerateMethod.LockedTo30Fps => 30,
                    FramerateMethod.LockedTo60Fps => 60,
                    _ => 60,
                };
            }
            return Browser.Create(WebPeeperModule.Instance.Settings.HomeUrl.Value, frameRate);
        }
    }
}