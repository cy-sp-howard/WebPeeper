using Blish_HUD;
using CefHelper;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    internal class CefService
    {
        static WebPeeperModule Module => WebPeeperModule.Instance;
        static ModuleSettings Settings => Module.Settings;
        static readonly Dictionary<CefAvailableVersion, CefPkgVersion> _versions = new() {
            { CefAvailableVersion.v103, new("103.0.90","103.0.9") },
            { CefAvailableVersion.v144, new("144.0.120","144.0.12") },
        };
        static public CefPkgVersion CurrentVersion { get; private set; } = _versions[Settings.CefVersion.Value];
        static string CefSharpVersionsFolder => DirectoryUtil.RegisterDirectory(Module.DataFolder, "CefVersions");
        static string CefCacheFolder => DirectoryUtil.RegisterDirectory(Module.DataFolder, "CefCache");
        static string _cefFolder = Path.Combine(CefSharpVersionsFolder, $"{CurrentVersion}");
        static string _cefSharpFolder = Path.Combine(CefSharpVersionsFolder, $"{CurrentVersion}");
        static string _cefSharpBhmPath = Path.Combine("cef", $"{CurrentVersion}");
        static readonly Dictionary<string, AssemblyLoadType> _pendingResolveDlls = [];
        static public event EventHandler LibLoadStart; // trigger once only
        static public bool LibLoadStarted { get; private set; } = false;
        bool _eventHandlersBound = false;
        static public IReadOnlyDictionary<CefAvailableVersion, CefPkgVersion> Versions => _versions;
        static readonly CefPkgVersion _suggestionVersion = _versions[CefAvailableVersion.v144];
        static public readonly CefPkgVersion DefaultVersion = _versions[CefAvailableVersion.v103];

        static bool IsDefaultVersion => CurrentVersion == DefaultVersion;
        static public bool Outdated => CurrentVersion < _suggestionVersion;
        public void Load()
        {
            CleanOldData();
            ExtractFiles();
            _ = Module.DownloadService.Download(CurrentVersion);
        }
        public void Unload()
        {
            LibLoadStart = null;
            WebPeeperModule.BlishHudInstance.Exiting -= OnBlishHudExiting;
            AppDomain.CurrentDomain.AssemblyResolve -= CefSharpLibResolver;
            if (LibLoadStarted) OnBlishHudExiting(this, EventArgs.Empty); // prevent load cefHelper so use OnBlishHudExiting 
        }
        public void ApplySettingVersion()
        {
            var newVersion = _versions[Settings.CefVersion.Value];
            if (LibLoadStarted) return;
            CurrentVersion = newVersion;
        }
        public string GetCefSharpFolder(CefPkgVersion version)
        {
            return Path.Combine(CefSharpVersionsFolder, version.ToString());
        }
        void CleanOldData()
        {
            WebPeeperModule.Logger.Debug("CefService.CleanOldData: cleaning WebPeeper old version data.");
            try
            {
                var path = Path.Combine(DirectoryUtil.CachePath, "cefsharp");
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex) { WebPeeperModule.Logger.Error(ex.Message); }
            try
            {
                var path = Path.Combine(Module.DataFolder, "CefUserData");
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex) { WebPeeperModule.Logger.Error(ex.Message); }
            try
            {
                var errVersion = _versions[Settings.CefErrorVersion.Value];
                Settings.CefErrorVersion.Value = CefAvailableVersion.v103;
                Module.DownloadService.Delete(errVersion);
            }
            catch (Exception ex) { WebPeeperModule.Logger.Error(ex.Message); }
        }
        void ClearCefCache()
        {
            try
            {
                Directory.Delete(CefCacheFolder, true);
            }
            catch { }
        }
        void SetupCefDll()
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

                WebPeeperModule.Logger.Debug($"CefService.SetupCefDll: cef {CurrentVersion} path {_cefFolder}");
            }

            if (GameService.GameIntegration.Gw2Instance.Gw2IsRunning) setLibCefDllFolder(this, EventArgs.Empty);
            else GameService.GameIntegration.Gw2Instance.Gw2Started += setLibCefDllFolder;
        }
        void BindEventHandlers()
        {
            if (_eventHandlersBound) return;
            WebPeeperModule.Logger.Debug($"CefService.BindEventHandlers: binding cefHelper event.");
            _eventHandlersBound = true;
            SetContextCreatedScript();
            WebPeeperModule.BlishHudInstance.Exiting += OnBlishHudExiting;
            Browser.BlishHudSchemeRequested += OnBlishHudSchemeRequested;
            Browser.FocusedChanged += OnFocusedChanged;
            Browser.TitleChanged += OnTitleChanged;
        }
        void SetContextCreatedScript()
        {
            using var fileStream = Module.ContentsManager.GetFileStream("onContextCreated.js") as MemoryStream;
            using TextReader reader = new StreamReader(fileStream, Encoding.UTF8);
            Browser.ContextCreatedScript = reader.ReadToEnd();
        }
        void ExtractFiles()
        {
            WebPeeperModule.Logger.Debug("CefService.ExtractFiles: extracting CefSharp default version.");
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
        void SetupCefSharpDll()
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

            WebPeeperModule.Logger.Debug($"CefService.SetupCefSharpDll: cefsharp {CurrentVersion} path {_cefSharpFolder}");
            WebPeeperModule.Logger.Debug($"CefService.SetupCefSharpDll: cefsharp {CurrentVersion} path .bhm\\{_cefSharpBhmPath}");
        }
        Assembly CefSharpLibResolver(object sender, ResolveEventArgs args)
        {
            var target = new AssemblyName(args.Name).Name;
            var pending = _pendingResolveDlls.TryGetValue(target, out AssemblyLoadType loadType);
            if (!pending)
            {
                WebPeeperModule.Logger.Debug($"CefService.CefSharpLibResolver: not in pending, skip load");
                return null;
            }
            // Load(byte[]) never reused loaded, so drop loaded
            // https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.load?view=net-8.0#system-reflection-assembly-load(system-byte())
            _pendingResolveDlls.Remove(target);
            target += ".dll";
            if (loadType == AssemblyLoadType.Bytes)
            {
                var filePath = Path.Combine(_cefSharpBhmPath, target);
                var fileBytes = WebPeeperModule.InstanceModuleManager.DataReader.GetFileBytes(filePath);
                WebPeeperModule.Logger.Debug($"CefService.CefSharpLibResolver: load .bhm\\{filePath}");
                return Assembly.Load(fileBytes);
            }
            else if (loadType == AssemblyLoadType.Path)
            {
                var filePath = Path.Combine(_cefSharpFolder, target);
                WebPeeperModule.Logger.Debug($"CefService.CefSharpLibResolver: load {filePath}");
                return Assembly.LoadFrom(filePath);
            }
            return null;
        }
        public void ShowDevTools()
        {
            Browser.ShowDevTools();
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
        async public void Search(string text)
        {
            using var client = new HttpClient();
            try
            {
                var isValidUrl = Uri.TryCreate(text, UriKind.Absolute, out Uri _);
                if (!isValidUrl)
                {
                    var uriBuilder = new UriBuilder(text);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var response = await client.GetAsync(uriBuilder.Uri, HttpCompletionOption.ResponseHeadersRead, cts.Token); // throw if status error
                }
                Browser.LoadUrlAsync(text);
            }
            catch
            {
                Browser.LoadUrlAsync(new Regex("{\\s*text\\s*}").Replace(Settings.SearchUrl.Value, Uri.EscapeDataString(text)));
            }
        }
        public void ApplyFrameRate()
        {
            Browser.SetFrameRate(Settings.GetFrameRate());
        }
        public void ApplyUserAgent()
        {
            Browser.SetMobileUserAgent(Settings.IsMobileLayout.Value);
        }
        void OnBlishHudExiting(object sender, EventArgs e)
        {
            Browser.Dispose(); // make sure close for restart
        }
        Stream OnBlishHudSchemeRequested(string filePath)
        {
            return Module.ContentsManager.GetFileStream(filePath); // cef auto dispose stream
        }
        void OnFocusedChanged(bool focused)
        {
            WebPeeperModule.Logger.Debug($"CefService.OnFocusedChanged: focused={focused}");
            WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
            {
                if (focused) Module.ImeService.Enable();
                else Module.ImeService.Disable();
            });
        }
        void OnTitleChanged(string title)
        {
            var uiService = Module.UiService;
            if (uiService is null || uiService.BrowserWindow is null) return;
            uiService.BrowserWindow.Subtitle = title;
        }
        string ChangePathTail(string path, string directoryName)
        {
            return Path.Combine(Path.GetDirectoryName(path) ?? "", directoryName);
        }
        void SetupLib()
        {
            if (LibLoadStarted) return;
            WebPeeperModule.Logger.Debug("CefService.SetupLib");
            LibLoadStarted = true;
            CefVersionSettingView.UpdateView?.Invoke();
            LibLoadStart?.Invoke(this, EventArgs.Empty);

            if (Settings.IsCleanMode.Value) ClearCefCache();
            SetupCefDll();
            SetupCefSharpDll();
        }
        Task CreateWebBrowser()
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                Browser.CefSettingInit(
                    IsDefaultVersion ? _cefFolder : Path.Combine(_cefFolder, "locales"),
                    CefCacheFolder,
                    _cefSharpFolder
                    );
                Browser.Create(Settings.HomeUrl.Value, Settings.GetFrameRate(), Settings.IsMobileLayout.Value)
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
                Settings.RedownloadCef();
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }
        async public void CloseWebBrowser()
        {
            var window = Module.UiService?.BrowserWindow;
            if (window is not null) await window.PrepareQuitBrowser();
            Browser.Close();
        }
        public Task StartBrowsing()
        {
            WebPeeperModule.Logger.Debug("CefService.StartBrowsing");
            return Task.Run(async () =>
             {
                 SetupLib();
                 BindEventHandlers();
                 await CreateWebBrowser();
             });
        }
    }
    enum AssemblyLoadType
    {
        Bytes,
        Path
    }
    public enum CefAvailableVersion
    {
        [Description(" 103.0.90")]
        v103,
        [Description(" 144.0.120")]
        v144
    }
}