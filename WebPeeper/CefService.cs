using Blish_HUD;
using Blish_HUD.Graphics;
using CefHelper;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    public class CefService
    {
        public string LastAddressInputText = "";
        static public string CefSettingFolder = DirectoryUtil.RegisterDirectory(WebPeeperModule.InstanceModuleManager.Manifest.Name.Replace(" ", "").ToLower());
        static public string CefSharpVersionsFolder = DirectoryUtil.RegisterDirectory(CefSettingFolder, "CefVersions");
        string _cefFolder;
        string _cefSharpFolder;
        string _cefSharpBhmPath;
        static readonly Dictionary<CefAvailableVersion, Version> _cefVersions = new() {
            { CefAvailableVersion.v103, new("103.0.90") },
            { CefAvailableVersion.v143, new("143.0.90") }
        };
        readonly Version _suggestionVersion = _cefVersions[CefAvailableVersion.v143];
        readonly Version _defaultVersion = _cefVersions[CefAvailableVersion.v103];
        Version _currentVersion = _cefVersions[WebPeeperModule.Instance.Settings.CefVersion.Value];
        readonly Dictionary<string, AssemblyLoadType> _pendingResolveAssemblies = [];
        public bool Outdated => _currentVersion < _suggestionVersion;
        public void Load()
        {
            _currentVersion = _suggestionVersion;
            SetupCefDllPath();
            SetupCefSharpDllFolder();
            SetupEventHandler();
            LoadContextCreatedScript();
        }
        public void Unload()
        {
            WebPeeperModule.BlishHudInstance.Exiting -= OnBlishHudExiting;
            AppDomain.CurrentDomain.AssemblyResolve -= CefSharpLibResolver;
            Browser.Dispose();
        }
        void SetupCefDllPath()
        {
            void setLibCefDllFolder(object s, EventArgs e)
            {
                GameService.GameIntegration.Gw2Instance.Gw2Started -= setLibCefDllFolder;
                if (_currentVersion == _defaultVersion)
                {
#if DEBUG
                    string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string nugetFolder = Path.Combine(userFolder, ".nuget", "packages");
                    _cefFolder = Path.Combine(nugetFolder, "cef.redist.x64\\103.0.9\\CEF");
#else
                    var gw2Folder = Path.GetDirectoryName(GameService.GameIntegration.Gw2Instance.Gw2Process.MainModule.FileName);
                    _cefFolder = Path.Combine(gw2Folder, "bin64\\cef");
                    _cefLocalesPath = cefFolder;
#endif
                }
                else
                {
                    _cefFolder = Path.Combine(CefSharpVersionsFolder, _currentVersion.ToString());
                }
                //Utils.SetDllDirectory(cefFolder); // not working in Wine
                Environment.SetEnvironmentVariable("PATH", $"{_cefFolder};{Environment.GetEnvironmentVariable("PATH")}");
            }

            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) setLibCefDllFolder(this, EventArgs.Empty);
            else GameService.GameIntegration.Gw2Instance.Gw2Started += setLibCefDllFolder;
        }
        void SetupEventHandler()
        {
            WebPeeperModule.BlishHudInstance.Exiting += OnBlishHudExiting;
            Browser.BlishHudSchemeRequested += OnBlishHudSchemeRequested;
            Browser.FocusedChanged += OnFocusedChanged;
            Browser.TitleChanged += OnTitleChanged;
            Browser.FrameLoadStart += OnFrameLoadStart;
            Browser.UrlLoadError += OnUrlLoadError;
            Browser.FullscreenModeChanged += OnFullscreenModeChange;
        }
        void LoadContextCreatedScript()
        {
            using var fileStream = WebPeeperModule.Instance.ContentsManager.GetFileStream("onContextCreated.js") as MemoryStream;
            using TextReader reader = new StreamReader(fileStream, Encoding.UTF8);
            Browser.ContextCreatedScript = reader.ReadToEnd();
        }
        void DownloadFiles()
        {
            var version = _currentVersion.ToString();
            // check file existed if not download
            // check file not broken
        }
        void ExtractFiles()
        {
            string[] files = ["CefSharp.dll", "CefSharp.BrowserSubprocess.Core.dll", "CefSharp.BrowserSubprocess.exe", "CefSharp.Core.Runtime.dll"];
            string[] paths = [.. files.Select(f => Path.Combine(_cefSharpBhmPath, f))];
            Directory.CreateDirectory(_cefSharpFolder);
            foreach (var path in paths)
            {
                var destinationFile = Path.Combine(_cefSharpFolder, Path.GetFileName(path));
                byte[] file = WebPeeperModule.InstanceModuleManager.DataReader.GetFileBytes(path);
                try
                {
                    using var fileStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.Write, 4096);
                    fileStream.Write(file, 0, file.Length);
                }
                catch { }
            }
        }
        void SetupCefSharpDllFolder()
        {
            var isDefaultVersion = _currentVersion == _defaultVersion;
            _pendingResolveAssemblies.Add("CefHelper", AssemblyLoadType.Bytes);
            if (isDefaultVersion)
            {
                // will priority load CefSharp.dll in CefSharp.Core.Runtime.dll located folder when load CefSharp.Core.Runtime.dll,
                // lead to load same dll twice, one is through bytes (cannot identify same file) another through path
                _pendingResolveAssemblies.Add("CefSharp", AssemblyLoadType.Path);
                _pendingResolveAssemblies.Add("CefSharp.OffScreen", AssemblyLoadType.Bytes);
                _pendingResolveAssemblies.Add("CefSharp.Core", AssemblyLoadType.Bytes);
                _pendingResolveAssemblies.Add("CefSharp.Core.Runtime", AssemblyLoadType.Path);
            }
            else
            {
                // doesnt use $PATH probing managed dll
                _pendingResolveAssemblies.Add("CefSharp", AssemblyLoadType.Path);
                _pendingResolveAssemblies.Add("CefSharp.OffScreen", AssemblyLoadType.Path);
                _pendingResolveAssemblies.Add("CefSharp.Core", AssemblyLoadType.Path);
                _pendingResolveAssemblies.Add("CefSharp.Core.Runtime", AssemblyLoadType.Path);
            }
            var version = _currentVersion.ToString();
            _cefSharpFolder = Path.Combine(CefSharpVersionsFolder, version);
            _cefSharpBhmPath = Path.Combine("cef", version);
            if (isDefaultVersion) ExtractFiles();
            else DownloadFiles();
            AppDomain.CurrentDomain.AssemblyResolve += CefSharpLibResolver;
        }
        Assembly CefSharpLibResolver(object sender, ResolveEventArgs args)
        {
            var target = new Regex("[^,]+").Match(args.Name).Value;
            var pending = _pendingResolveAssemblies.TryGetValue(target, out AssemblyLoadType loadType);
            if (!pending) return null;
            // Load(byte[]) never reused loaded, so drop loaded
            // https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.load?view=net-8.0#system-reflection-assembly-load(system-byte())
            _pendingResolveAssemblies.Remove(target);
            target = new Regex("(\\.dll)*$", RegexOptions.IgnoreCase).Replace(target, ".dll");
            if (loadType == AssemblyLoadType.Bytes)
            {
                var fileBytes = WebPeeperModule.InstanceModuleManager.DataReader.GetFileBytes(Path.Combine(_cefSharpBhmPath, target));
                return Assembly.Load(fileBytes);
            }
            else if (loadType == AssemblyLoadType.Path)
            {
                return Assembly.LoadFrom(Path.Combine(_cefSharpFolder, target));
            }
            return null;
        }
        public Task<Texture2D> GetScreenshot()
        {
            if (!Browser.Ready) return Task.FromResult<Texture2D>(null);
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
            if (Browser.Created) Browser.Close();
        }
        public void ApplyUserAgent()
        {
            if (!Browser.Ready) return;
            Browser.SetMobileUserAgent(WebPeeperModule.Instance.Settings.IsMobileLayout.Value);
        }
        void OnBlishHudExiting(object sender, EventArgs e)
        {
            if (!Browser.Created) return;
            Browser.Dispose(); // make sure close for restart
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
                    if (!Browser.Ready) return;
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
            Browser.CefSettingInit(
                _currentVersion == _defaultVersion ? _cefFolder : Path.Combine(_cefFolder, "locales"),
                CefSettingFolder,
                _cefSharpFolder,
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
            return Browser.Create(WebPeeperModule.Instance.Settings.HomeUrl.Value, frameRate, WebPeeperModule.Instance.Settings.IsMobileLayout.Value);
        }
    }
    enum AssemblyLoadType
    {
        Bytes,
        Path
    }
}