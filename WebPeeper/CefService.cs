using Blish_HUD;
using Blish_HUD.Graphics;
using CefHelper;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    internal class CefService
    {
        static readonly public string CefSettingFolder = DirectoryUtil.RegisterDirectory(WebPeeperModule.InstanceModuleManager.Manifest.Name.Replace(" ", "").ToLower());
        static readonly string _cefSharpVersionsFolder = DirectoryUtil.RegisterDirectory(CefSettingFolder, "CefVersions");
        string _cefFolder = Path.Combine(_cefSharpVersionsFolder, $"{CurrentVersion}");
        string _cefSharpFolder = Path.Combine(_cefSharpVersionsFolder, $"{CurrentVersion}");
        string _cefSharpBhmPath = Path.Combine("cef", $"{CurrentVersion}");
        readonly Dictionary<string, AssemblyLoadType> _pendingResolveDlls = [];
        public event EventHandler LibLoadStart;
        static public bool LibLoadStarted { get; private set; } = false;
        static readonly Dictionary<CefAvailableVersion, CefPkgVersion> _versions = new() {
            { CefAvailableVersion.v103, new("103.0.90","103.0.9") },
            { CefAvailableVersion.v143, new("143.0.90","143.0.9") },
            { CefAvailableVersion.v144, new("144.0.120","144.0.12") },
        };
        static public IReadOnlyDictionary<CefAvailableVersion, CefPkgVersion> Versions => _versions;
        static readonly CefPkgVersion _suggestionVersion = Versions[CefAvailableVersion.v144];
        static public readonly CefPkgVersion DefaultVersion = Versions[CefAvailableVersion.v103];
        static public CefPkgVersion CurrentVersion { get; private set; } = Versions[WebPeeperModule.Instance.Settings.CefVersion.Value];

        static bool IsDefaultVersion => CurrentVersion == DefaultVersion;
        public bool Outdated => CurrentVersion < _suggestionVersion;
        public string LastAddressInputText = "";
        public void Load()
        {
            CleanOldData();
            ExtractFiles();
            _ = WebPeeperModule.Instance.DownloadService.Download(CurrentVersion);
        }
        public void Unload()
        {
            LibLoadStart = null;
            WebPeeperModule.BlishHudInstance.Exiting -= OnBlishHudExiting;
            AppDomain.CurrentDomain.AssemblyResolve -= CefSharpLibResolver;
            if (LibLoadStarted) OnBlishHudExiting(this, EventArgs.Empty);
        }
        public void ApplySettingVersion()
        {
            var newVersion = Versions[WebPeeperModule.Instance.Settings.CefVersion.Value];
            if (LibLoadStarted) return;
            CurrentVersion = newVersion;
        }
        public string GetCefSharpFolder(CefPkgVersion version)
        {
            return Path.Combine(_cefSharpVersionsFolder, version.ToString());
        }
        void CleanOldData()
        {
            try
            {
                var path = Path.Combine(DirectoryUtil.CachePath, "cefsharp");
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { }
            try
            {
                var errVersion = Versions[WebPeeperModule.Instance.Settings.CefErrorVersion.Value];
                WebPeeperModule.Instance.Settings.CefErrorVersion.Value = CefAvailableVersion.v103;
                WebPeeperModule.Instance.DownloadService.Delete(errVersion);
            }
            catch { }
        }
        void SetupCefDllPath()
        {
            void setLibCefDllFolder(object s, EventArgs e)
            {
                GameService.GameIntegration.Gw2Instance.Gw2Started -= setLibCefDllFolder;
                if (IsDefaultVersion)
                {
#if DEBUG
                    string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string nugetFolder = Path.Combine(userFolder, ".nuget", "packages");
                    _cefFolder = Path.Combine(nugetFolder, "cef.redist.x64\\103.0.9\\CEF");
#else
                    var gw2Folder = Path.GetDirectoryName(GameService.GameIntegration.Gw2Instance.Gw2Process.MainModule.FileName);
                    _cefFolder = Path.Combine(gw2Folder, "bin64\\cef");
#endif
                }
                else
                {
                    _cefFolder = ChangePathTail(_cefFolder, $"{CurrentVersion}");
                }
                //Utils.SetDllDirectory(cefFolder); // not working in Wine
                Environment.SetEnvironmentVariable("PATH", $"{_cefFolder};{Environment.GetEnvironmentVariable("PATH")}");
            }

            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) setLibCefDllFolder(this, EventArgs.Empty);
            else GameService.GameIntegration.Gw2Instance.Gw2Started += setLibCefDllFolder;
        }
        void SetupEventHandlers()
        {
            SetContextCreatedScript();
            WebPeeperModule.BlishHudInstance.Exiting += OnBlishHudExiting;
            Browser.BlishHudSchemeRequested += OnBlishHudSchemeRequested;
            Browser.FocusedChanged += OnFocusedChanged;
            Browser.TitleChanged += OnTitleChanged;
            Browser.FrameLoadStart += OnFrameLoadStart;
            Browser.UrlLoadError += OnUrlLoadError;
            Browser.FullscreenModeChanged += OnFullscreenModeChange;
        }
        void SetContextCreatedScript()
        {
            using var fileStream = WebPeeperModule.Instance.ContentsManager.GetFileStream("onContextCreated.js") as MemoryStream;
            using TextReader reader = new StreamReader(fileStream, Encoding.UTF8);
            Browser.ContextCreatedScript = reader.ReadToEnd();
        }
        void ExtractFiles()
        {
            string[] files = ["CefSharp.dll", "CefSharp.BrowserSubprocess.Core.dll", "CefSharp.BrowserSubprocess.exe", "CefSharp.Core.Runtime.dll"];
            string[] paths = [.. files.Select(f => Path.Combine(ChangePathTail(_cefSharpBhmPath, $"{DefaultVersion}"), f))];
            var destinationFolder = ChangePathTail(_cefSharpFolder, $"{DefaultVersion}");
            Directory.CreateDirectory(destinationFolder);
            foreach (var path in paths)
            {
                var destinationFile = Path.Combine(destinationFolder, Path.GetFileName(path));
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
            _pendingResolveDlls.Add("CefHelper", AssemblyLoadType.Bytes);
            if (IsDefaultVersion)
            {
                // will priority load CefSharp.dll in CefSharp.Core.Runtime.dll located folder when load CefSharp.Core.Runtime.dll,
                // lead to load same dll twice, one is through bytes (cannot identify same file) another through path
                _pendingResolveDlls.Add("CefSharp", AssemblyLoadType.Path);
                _pendingResolveDlls.Add("CefSharp.OffScreen", AssemblyLoadType.Bytes);
                _pendingResolveDlls.Add("CefSharp.Core", AssemblyLoadType.Bytes);
                _pendingResolveDlls.Add("CefSharp.Core.Runtime", AssemblyLoadType.Path);
            }
            else
            {
                // doesnt use $PATH probing managed dll
                _pendingResolveDlls.Add("CefSharp", AssemblyLoadType.Path);
                _pendingResolveDlls.Add("CefSharp.OffScreen", AssemblyLoadType.Path);
                _pendingResolveDlls.Add("CefSharp.Core", AssemblyLoadType.Path);
                _pendingResolveDlls.Add("CefSharp.Core.Runtime", AssemblyLoadType.Path);
            }
            _cefSharpFolder = ChangePathTail(_cefSharpFolder, $"{CurrentVersion}");
            _cefSharpBhmPath = ChangePathTail(_cefSharpBhmPath, $"{CurrentVersion}");
            AppDomain.CurrentDomain.AssemblyResolve += CefSharpLibResolver;
        }
        Assembly CefSharpLibResolver(object sender, ResolveEventArgs args)
        {
            var target = new AssemblyName(args.Name).Name;
            var pending = _pendingResolveDlls.TryGetValue(target, out AssemblyLoadType loadType);
            if (!pending) return null;
            // Load(byte[]) never reused loaded, so drop loaded
            // https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.load?view=net-8.0#system-reflection-assembly-load(system-byte())
            _pendingResolveDlls.Remove(target);
            target += ".dll";
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
            Browser.SetMobileUserAgent(WebPeeperModule.Instance.Settings.IsMobileLayout.Value);
        }
        void OnBlishHudExiting(object sender, EventArgs e)
        {
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
            var text = string.IsNullOrWhiteSpace(LastAddressInputText) ? failedUrl : LastAddressInputText;
            LastAddressInputText = "";
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri _)) // doesnt search if input url
            {
                WebPainter.Instance?.SetErrorState(true);
            }
            else
            {
                Browser.LoadUrlAsync(new Regex("{\\s*text\\s*}").Replace(WebPeeperModule.Instance.Settings.SearchUrl.Value, Uri.EscapeDataString(text)));
            }
        }
        void OnFullscreenModeChange(bool isFullscreen)
        {
            if (isFullscreen) NavigationBar.Instance?.Hide();
            else NavigationBar.Instance?.Show();
        }
        string ChangePathTail(string path, string directoryName)
        {
            return Path.Combine(Path.GetDirectoryName(path) ?? "", directoryName);
        }
        void SetupLib()
        {
            if (LibLoadStarted) return;
            LibLoadStarted = true;
            CefVersionSettingView.UpdateView?.Invoke();
            LibLoadStart?.Invoke(this, EventArgs.Empty);
            SetupCefDllPath();
            SetupCefSharpDllFolder();
            SetupEventHandlers();
        }
        Task CreateWebBrowser()
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                var settings = WebPeeperModule.Instance.Settings;
                Browser.CefSettingInit(
                    IsDefaultVersion ? _cefFolder : Path.Combine(_cefFolder, "locales"),
                    CefSettingFolder,
                    _cefSharpFolder,
                    settings.IsCleanMode.Value
                    );
                var frameRate = 30;
                if (settings.IsFollowBhFps.Value)
                {
                    frameRate = GameService.Graphics.FrameLimiter switch
                    {
                        FramerateMethod.LockedTo30Fps => 30,
                        FramerateMethod.LockedTo60Fps => 60,
                        _ => 60,
                    };
                }
                Browser.Create(settings.HomeUrl.Value, frameRate, settings.IsMobileLayout.Value)
                    .ContinueWith(t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion) tcs.TrySetResult(true);
                        else
                        {
                            WebPeeperModule.Logger.Error(t.Exception?.Message);
                            tcs.TrySetException(t.Exception);
                        }
                    });
                Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(t => tcs.TrySetCanceled());
            }
            catch (Exception ex)
            {
                WebPeeperModule.Logger.Error(ex.Message);
                WebPeeperModule.Instance.Settings.RedownloadCef();
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }
        public async Task StartBrowsing()
        {
            SetupLib();
            await CreateWebBrowser();
        }
    }
    internal class CefPkgVersion(string cefSharpversion, string cefVersion = "")
    {
        readonly public Version CefSharp = new(cefSharpversion);
        readonly public Version Cef = new(string.IsNullOrEmpty(cefVersion) ? cefSharpversion : cefVersion);
        public override string ToString()
        {
            return CefSharp.ToString();
        }
        public override bool Equals(object obj)
        {
            if (obj is CefPkgVersion cefVerObj)
            {
                return cefVerObj == this;
            }
            return false;
        }
        public override int GetHashCode()
        {
            return this.CefSharp.GetHashCode();
        }
        public static bool operator >(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp > v2.CefSharp;
        }
        public static bool operator <(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp < v2.CefSharp;
        }
        public static bool operator >=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp > v2.CefSharp;
        }
        public static bool operator <=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp < v2.CefSharp;
        }
        public static bool operator ==(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp == v2.CefSharp;
        }
        public static bool operator !=(CefPkgVersion v1, CefPkgVersion v2)
        {
            return v1.CefSharp != v2.CefSharp;
        }
    }
    enum AssemblyLoadType
    {
        Bytes,
        Path
    }
    public enum CefAvailableVersion
    {
        [Description(" 144.0.120")]
        v144,
        [Description(" 143.0.90")]
        v143,
        [Description(" 103.0.90")]
        v103
    }
}