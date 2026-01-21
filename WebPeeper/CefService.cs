using Blish_HUD;
using Blish_HUD.Graphics;
using Blish_HUD.Input;
using CefSharp;
using CefSharp.Internals;
using CefSharp.OffScreen;
using CefSharp.Structs;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BhModule.WebPeeper
{
    public class CefService
    {
        private ChromiumWebBrowser _webBrowser;
        public ChromiumWebBrowser WebBrowser { get => _webBrowser; }
        public InputMethod InputMethod { get => _inputMethod; }
        readonly InputMethod _inputMethod;
        public string LastAddressInputText = "";
        static public string CefSharpDllPath = DirectoryUtil.RegisterDirectory(DirectoryUtil.CachePath, "cefsharp/");
        static public string CefSettingFolder = DirectoryUtil.RegisterDirectory(WebPeeperModule.InstanceModuleManager.Manifest.Name.Replace(" ", "").ToLower());
        const string _mobileUserAgent = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Mobile Safari/537.36";
        const string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/103.0.5060.114 Safari/537.36";
        string OnContextCreatedScript;
        string _cefLocalesPath;
        public CefService()
        {
            SetupCefDllPath();
            SetupCefSharpDllFolder();
            _inputMethod = new InputMethod();
            WebPeeperModule.BlishHudInstance.Exiting += OnBlishHudExiting;
        }
        public void Load()
        {
            LoadOnContextCreatedScript();
        }
        public void Unload()
        {
            WebPeeperModule.BlishHudInstance.Exiting -= OnBlishHudExiting;
            AppDomain.CurrentDomain.AssemblyResolve -= CefSharpCoreRuntimeResolver;
            _webBrowser?.Dispose();
            _inputMethod.Dispose();
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
        void CefSettingInit()
        {
            if (Cef.IsInitialized) return;
            var moduleSettings = WebPeeperModule.Instance.Settings;
            CefSharpSettings.FocusedNodeChangedEnabled = true;
            var settings = new CefSettings();
            settings.EnableAudio();
            settings.UserAgent = moduleSettings.IsMobileLayout.Value ? _mobileUserAgent : _defaultUserAgent;
            if (!string.IsNullOrEmpty(_cefLocalesPath)) settings.LocalesDirPath = _cefLocalesPath;
            settings.BrowserSubprocessPath = Path.Combine(CefSharpDllPath, "CefSharp.BrowserSubprocess.exe");
            settings.CachePath = Path.Combine(CefSettingFolder, "CefCache");
            settings.UserDataPath = Path.Combine(CefSettingFolder, "CefUserData");
            settings.CefCommandLineArgs.Add("gpu-preferences"); // not sure what is it, but gw2 cefhost.exe uses it
            if (WebPeeperModule.Instance.Settings.IsCleanMode.Value)
            {
                Directory.Delete(settings.CachePath, true);
                Directory.Delete(settings.UserDataPath, true);
            }
            settings.PersistSessionCookies = true;
            CefHelper.Default.SetCefSchemeHandler(settings, OnBlishHudSchemeRequested);
            Cef.Initialize(settings);
        }
        void LoadOnContextCreatedScript()
        {
            using var fileStream = WebPeeperModule.Instance.ContentsManager.GetFileStream("onContextCreated.js") as MemoryStream;
            using TextReader reader = new StreamReader(fileStream, Encoding.UTF8);
            OnContextCreatedScript = reader.ReadToEnd();
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
        public void FocusBlurredElement()
        {
            if (_webBrowser is null || !_webBrowser.CanExecuteJavascriptInMainFrame) return;
            _webBrowser.ExecuteScriptAsync("webPeeper_focusBlurredElement()");
        }
        public Task SetBrowserSize(int w, int h)
        {
            if (WebBrowser is null) return Task.FromResult(false);
            return WebBrowser.ResizeAsync(w, h);
        }
        public Task<Texture2D> GetScreenshot()
        {
            if (WebBrowser is null) return Task.FromResult<Texture2D>(null);
            return WebBrowser.CaptureScreenshotAsync().ContinueWith(t =>
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
            await WebPeeperModule.Instance.UIService.BrowserWindow.PrepareQuitBrowser();
            if (_webBrowser is null) return;
            _webBrowser.Dispose();
            _webBrowser = null;
        }
        public void ApplyUserAgent()
        {
            if (_webBrowser is null) return;
            using var devToolsClient = _webBrowser.GetDevToolsClient();
            var agentString = WebPeeperModule.Instance.Settings.IsMobileLayout.Value ? _mobileUserAgent : _defaultUserAgent;
            devToolsClient.Emulation.SetUserAgentOverrideAsync(agentString);
        }
        void OnBlishHudExiting(object sender, EventArgs e)
        {
            _webBrowser?.Dispose(); // make sure close for restart
        }
        (Stream, string) OnBlishHudSchemeRequested(IRequest request)
        {
            var uri = new Uri(request.Url);
            var filePath = uri.AbsolutePath.Remove(0, 1);
            var stream = WebPeeperModule.Instance.ContentsManager.GetFileStream(filePath);
            return (stream, Cef.GetMimeType(Path.GetExtension(filePath)));
        }
        void OnContextCreated(IFrame frame)
        {
            frame.ExecuteJavaScriptAsync(OnContextCreatedScript);
        }
        void OnFocusedNodeChanged(IDomNode node)
        {
            WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
            {
                if (node is null) { _inputMethod.Disable(); return; }
                bool isContenteditable = node["contenteditable"] is not null && node["contenteditable"] != "false";
                if (isContenteditable || node.TagName == "INPUT" || node.TagName == "TEXTAREA")
                {
                    _inputMethod.Enable();
                }
                else _inputMethod.Disable();
            });
        }
        void OnMainFrameChanged()
        {
            WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() => _inputMethod.Disable());
        }
        void OnTitleChanged(object sender, TitleChangedEventArgs e)
        {
            var uiService = WebPeeperModule.Instance.UIService;
            if (uiService is null || uiService.BrowserWindow is null) return;
            uiService.BrowserWindow.Subtitle = e.Title;
        }
        void OnFrameLoadStart(object sender, FrameLoadStartEventArgs e)
        {
            WebPainter.Instance?.SetErrorState(false);
        }
        public void OnUrlLoadError(object sender, LoadErrorEventArgs e)
        {
            NavigationBar.Instance?.SetAddressInputText(e.FailedUrl);
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
                    _webBrowser.LoadUrlAsync(new Regex("{\\s*text\\s*}").Replace(WebPeeperModule.Instance.Settings.SearchUrl.Value, Uri.EscapeDataString(text)));
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
            CefSettingInit();
            var tcs = new TaskCompletionSource<bool>();
            if (_webBrowser == null || _webBrowser.IsDisposed)
            {
                var browserSetting = new BrowserSettings(true);
                if (WebPeeperModule.Instance.Settings.IsFollowBhFps.Value)
                {
                    browserSetting.WindowlessFrameRate = GameService.Graphics.FrameLimiter switch
                    {
                        FramerateMethod.LockedTo30Fps => 30,
                        FramerateMethod.LockedTo60Fps => 60,
                        _ => 60,
                    };
                }
                _webBrowser = new ChromiumWebBrowser(WebPeeperModule.Instance.Settings.HomeUrl.Value, browserSetting);
                _webBrowser.TitleChanged += OnTitleChanged;
                _webBrowser.FrameLoadStart += OnFrameLoadStart;
                _webBrowser.LoadError += OnUrlLoadError;
                // _webBrowser.ConsoleMessage += (sender, e) =>
                // {
                //     Trace.WriteLine("CONSOLE: " + e.Message);
                // };
                _webBrowser.BrowserInitialized += delegate { tcs.TrySetResult(true); };
                // CefHelper.dll that prevent system load CefSharp.dll before create browser. 
                CefHelper.Default.SetBrowserHandlers(
                    _webBrowser,
                    OnContextCreated,
                    OnFocusedNodeChanged,
                    OnMainFrameChanged,
                    OnFullscreenModeChange
                    );
            }
            if (_webBrowser.IsBrowserInitialized) tcs.TrySetResult(true);
            return tcs.Task;
        }

    }
    // https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.nativewindow?view=windowsdesktop-10.0
    // https://github.com/cefsharp/CefSharp/blob/v143.0.90/CefSharp.Wpf/Internals/IMEHandler.cs
    public class InputMethod : NativeWindow
    {
        IntPtr WinHandle { get => WebPeeperModule.BlishHudInstance.FormHandle; }
        IntPtr _himc;
        Message _m;
        object _keyStateChangedCloned;
        object _keyReleasedCloned;
        object _keyPressedCloned;
        bool _mouseLeftPressed = false;
        Action _mouseLeftReleaseCallback;
        readonly Dictionary<string, (Action<object>, Func<object>)> _keybindsBackupMap = [];
        ChromiumWebBrowser Browser { get => WebPeeperModule.Instance.CefService.WebBrowser; }
        public InputMethod()
        {
            AssignHandle(WinHandle);
            WebPeeperModule.BlishHudInstance.Form.LostFocus += OnHudLostFocus;
            GameService.Input.Mouse.LeftMouseButtonPressed += OnLeftMouseButtonPressed;
            GameService.Input.Mouse.LeftMouseButtonReleased += OnLeftMouseButtonReleased;
            _keybindsBackupMap.Add("KeyStateChanged", (v => { _keyStateChangedCloned = v; }, () => _keyStateChangedCloned));
            _keybindsBackupMap.Add("KeyReleased", (v => { _keyReleasedCloned = v; }, () => _keyReleasedCloned));
            _keybindsBackupMap.Add("KeyPressed", (v => { _keyPressedCloned = v; }, () => _keyPressedCloned));
        }
        public void Dispose()
        {
            ReleaseHandle();
            WebPeeperModule.BlishHudInstance.Form.LostFocus -= OnHudLostFocus;
            GameService.Input.Mouse.LeftMouseButtonPressed -= OnLeftMouseButtonPressed;
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnLeftMouseButtonReleased;
        }
        protected override void WndProc(ref Message m)
        {
            if (Browser is not null)
            {
                _m = m;
                switch ((WM)m.Msg)
                {
                    case WM.KEYUP:
                    case WM.KEYDOWN:
                    case WM.CHAR:
                        SendKey();
                        break;
                    case WM.IME_COMPOSITION:
                        SetCefComposition();
                        return;
                    case WM.IME_ENDCOMPOSITION:
                        Browser?.GetBrowserHost().ImeSetComposition("", [], new(int.MaxValue, int.MaxValue), new(0, 0));
                        Browser?.GetBrowserHost().ImeFinishComposingText(false);
                        return;
                    case WM.IME_STARTCOMPOSITION:
                        return;
                }
                _m = new();
            }
            base.WndProc(ref m);
        }
        bool LParmHasFlag(object flag)
        {
            var _flag = (int)flag;
            return (_m.LParam.ToInt64() & _flag) == _flag;
        }
        void OnLeftMouseButtonPressed(object sender, Blish_HUD.Input.MouseEventArgs e)
        {
            _mouseLeftPressed = true;
        }
        void OnLeftMouseButtonReleased(object sender, Blish_HUD.Input.MouseEventArgs e)
        {
            _mouseLeftPressed = false;
            _mouseLeftReleaseCallback?.Invoke();
            _mouseLeftReleaseCallback = null;
        }
        void OnHudLostFocus(object sender, EventArgs e)
        {
            if (Browser is null || !Browser.CanExecuteJavascriptInMainFrame) return;
            Browser.ExecuteScriptAsync("webPeeper_blur()");
        }
        void SendKey()
        {
            // Browser.GetBrowserHost().SendKeyEvent(_m.Msg, _m.WParam.CastToInt32(), _m.LParam.CastToInt32()); 
            // port from CefBrowserHostWrapper::SendKeyEvent , due to above throw System.InvalidProgramException: Invalid IL code in wine
            var wParam = _m.WParam.CastToInt32();
            var lParam = _m.LParam.CastToInt32();
            var evt = new KeyEvent()
            {
                Modifiers = GetCefKeyboardModifiers(wParam, lParam),
                WindowsKeyCode = wParam,
                NativeKeyCode = lParam,
                IsSystemKey = false,
                Type = (WM)_m.Msg switch
                {
                    WM.KEYDOWN => KeyEventType.KeyDown,
                    WM.KEYUP => KeyEventType.KeyUp,
                    WM.CHAR => KeyEventType.Char,
                    _ => (KeyEventType)(-1),
                }
            };
            Browser?.GetBrowserHost().SendKeyEvent(evt);
        }
        static bool IsKeyDown(VK wparam)
        {
            return (Utils.GetKeyState(wparam) & 0x8000) != 0;
        }
        static CefEventFlags GetCefKeyboardModifiers(int wParam, int lParam)
        {
            CefEventFlags modifiers = 0;
            if (IsKeyDown(VK.SHIFT)) modifiers |= CefEventFlags.ShiftDown;
            if (IsKeyDown(VK.CONTROL)) modifiers |= CefEventFlags.ControlDown;
            if (IsKeyDown(VK.MENU)) modifiers |= CefEventFlags.AltDown;

            if ((Utils.GetKeyState(VK.NUMLOCK) & 1) != 0) modifiers |= CefEventFlags.NumLockOn;
            if ((Utils.GetKeyState(VK.CAPITAL) & 1) != 0) modifiers |= CefEventFlags.CapsLockOn;

            switch ((VK)wParam)
            {
                case VK.RETURN:
                    // KF_EXTENDED = 0x0100
                    if (((lParam >> 16) & 0x0100) != 0) modifiers |= CefEventFlags.IsKeyPad;
                    break;
                case VK.INSERT:
                case VK.DELETE:
                case VK.HOME:
                case VK.END:
                case VK.PRIOR:
                case VK.NEXT:
                case VK.UP:
                case VK.DOWN:
                case VK.LEFT:
                case VK.RIGHT:
                    if (((lParam >> 16) & 0x0100) == 0) modifiers |= CefEventFlags.IsKeyPad;
                    break;
                case VK.NUMLOCK:
                case VK.NUMPAD0:
                case VK.NUMPAD1:
                case VK.NUMPAD2:
                case VK.NUMPAD3:
                case VK.NUMPAD4:
                case VK.NUMPAD5:
                case VK.NUMPAD6:
                case VK.NUMPAD7:
                case VK.NUMPAD8:
                case VK.NUMPAD9:
                case VK.DIVIDE:
                case VK.MULTIPLY:
                case VK.SUBTRACT:
                case VK.ADD:
                case VK.DECIMAL:
                case VK.CLEAR:
                    modifiers |= CefEventFlags.IsKeyPad;
                    break;
                case VK.SHIFT:
                    if (IsKeyDown(VK.LSHIFT)) modifiers |= CefEventFlags.IsLeft;
                    else if (IsKeyDown(VK.RSHIFT)) modifiers |= CefEventFlags.IsRight;
                    break;
                case VK.CONTROL:
                    if (IsKeyDown(VK.LCONTROL)) modifiers |= CefEventFlags.IsLeft;
                    else if (IsKeyDown(VK.RCONTROL)) modifiers |= CefEventFlags.IsRight;
                    break;
                case VK.MENU:
                    if (IsKeyDown(VK.LMENU)) modifiers |= CefEventFlags.IsLeft;
                    else if (IsKeyDown(VK.RMENU)) modifiers |= CefEventFlags.IsRight;
                    break;
                case VK.LWIN:
                    modifiers |= CefEventFlags.IsLeft;
                    break;
                case VK.RWIN:
                    modifiers |= CefEventFlags.IsRight;
                    break;
            }
            return modifiers;
        }
        void SetCefComposition()
        {
            var bHost = Browser?.GetBrowserHost();
            if (bHost is null) return;
            if (GetCompositionText(GCS.RESULTSTR, out string text))
            {
                bHost.ImeCommitText(text, new(int.MaxValue, int.MaxValue), 0);
                bHost.ImeSetComposition(text, [], new(int.MaxValue, int.MaxValue), new(0, 0));
                bHost.ImeFinishComposingText(false);
            }
            else if (GetCompositionText(GCS.COMPSTR, out text))
            {
                GetCompositionSelection(text, out List<CompositionUnderline> attrs, out int caret);
                bHost.ImeSetComposition(text, [.. attrs], new(int.MaxValue, int.MaxValue), new(caret, caret));
            }
        }
        bool GetCompositionText(GCS gcs, out string text)
        {
            var textLen = Utils.ImmGetCompositionStringW(_himc, (int)gcs, null, 0);
            if (textLen <= 0)
            {
                text = string.Empty;
                return false;
            }
            byte[] buffer = new byte[textLen];
            Utils.ImmGetCompositionStringW(_himc, (int)gcs, buffer, textLen);
            text = Encoding.Unicode.GetString(buffer);
            return true;
        }
        public void SetCompositionPostion()
        {
            if (Browser is null || WebPainter.Instance is null) return;
            var x = WebPainter.Instance.LocationAtForm.X;
            var y = WebPainter.Instance.LocationAtForm.Y;

            Browser.EvaluateScriptAsync("webPeeper_getFocusLocation()").ContinueWith(t =>
            {
                var inputXY = (List<object>)t.Result.Result;
                var inputX = (int)inputXY[0] + 2;
                var inputY = (int)inputXY[1];
                var offsetX = (int)(inputX * GameService.Graphics.UIScaleMultiplier);
                var offsetY = (int)(inputY * GameService.Graphics.UIScaleMultiplier);
                WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
                {
                    // CFS_POINT = 0x0002
                    Utils.ImmSetCompositionWindow(_himc, new CompositionForm() { Style = 0x0002, X = x + offsetX, Y = y + offsetY });
                });
            });
        }
        void GetCompositionSelection(string text, out List<CompositionUnderline> underlines, out int caretPosition)
        {
            byte[] attributes;
            int selectionStart = text.Length;
            int selectionEnd = text.Length;
            caretPosition = 0;
            underlines = [];

            if (LParmHasFlag(GCS.COMPATTR))
            {
                var attributeSize = Utils.ImmGetCompositionStringW(_himc, (int)GCS.COMPATTR, null, 0);
                if (attributeSize > 0)
                {
                    attributes = new byte[attributeSize];
                    Utils.ImmGetCompositionStringW(_himc, (int)GCS.COMPATTR, attributes, attributeSize);
                    for (selectionStart = 0; selectionStart < attributeSize; ++selectionStart)
                    {
                        // ATTR_TARGET_CONVERTED 0x01, ATTR_TARGET_NOTCONVERTED 0x03
                        if (attributes[selectionStart] == 0x01 || attributes[selectionStart] == 0x03)
                        {
                            break;
                        }
                    }
                    for (selectionEnd = selectionStart; selectionEnd < attributeSize; ++selectionEnd)
                    {
                        if (attributes[selectionStart] != 0x01 && attributes[selectionStart] != 0x03)
                        {
                            break;
                        }
                    }
                }
            }
            // CS_NOMOVECARET 0x4000
            if (!LParmHasFlag(0x4000) && LParmHasFlag(GCS.CURSORPOS))
            {
                caretPosition = Utils.ImmGetCompositionStringW(_himc, (int)GCS.CURSORPOS, null, 0);
            }

            if (LParmHasFlag(GCS.COMPCLAUSE))
            {
                var clauseSize = Utils.ImmGetCompositionStringW(_himc, (int)GCS.COMPCLAUSE, null, 0);
                int clauseLength = (int)clauseSize / sizeof(Int32);
                var clauseData = new byte[clauseSize];
                Utils.ImmGetCompositionStringW(_himc, (int)GCS.COMPCLAUSE, clauseData, clauseSize);
                for (int i = 0; i < clauseLength - 1; i++)
                {
                    int from = BitConverter.ToInt32(clauseData, i * sizeof(Int32));
                    int to = BitConverter.ToInt32(clauseData, (i + 1) * sizeof(Int32));

                    var range = new Range(from, to);
                    bool thick = (range.From >= selectionStart && range.To <= selectionEnd);
                    underlines.Add(new(range, 0xFF000000, 0x00000000, thick));
                }
            }

            if (underlines.Count == 0)
            {
                var range = new Range();
                bool thick = false;
                if (selectionStart > 0)
                {
                    range = new Range(0, selectionStart);
                }
                if (selectionEnd > selectionStart)
                {
                    range = new Range(selectionStart, selectionEnd);
                    thick = true;
                }
                if (selectionEnd < text.Length)
                {
                    range = new Range(selectionEnd, text.Length);
                }
                underlines.Add(new(range, 0xFF000000, 0x00000000, thick));
            }
        }
        void DisableAllKeybinds()
        {
            foreach (var item in _keybindsBackupMap)
            {
                var key = item.Key;
                var (setter, getter) = item.Value;
                if (getter() is not null) continue;
                // reflection
                var evtHandlerField = typeof(Blish_HUD.Input.KeyboardHandler).GetField(key, BindingFlags.Instance | BindingFlags.NonPublic);
                object evtHandler = evtHandlerField.GetValue(GameService.Input.Keyboard);
                if (evtHandler is EventHandler<KeyboardEventArgs> evtHandlerKnownType)
                {
                    setter(evtHandlerKnownType?.Clone());
                    evtHandlerField.SetValue(GameService.Input.Keyboard, null);
                }
            }
        }
        void RestoreAllKeybinds()
        {
            foreach (var item in _keybindsBackupMap)
            {
                var key = item.Key;
                var (setter, getter) = item.Value;
                var cloned = getter();
                if (cloned is null) return;
                // reflection
                var evtHandlerField = typeof(Blish_HUD.Input.KeyboardHandler).GetField(key, BindingFlags.Instance | BindingFlags.NonPublic);
                evtHandlerField.SetValue(GameService.Input.Keyboard, cloned);
                setter(null);
            }
        }
        public void Enable()
        {
            if (_mouseLeftPressed) { _mouseLeftReleaseCallback = Enable; return; }
            if (_himc != IntPtr.Zero) Disable(false);
            if (WebPeeperModule.Instance.Settings.IsBlockKeybinds.Value) DisableAllKeybinds();
            Utils.SetForegroundWindow(WinHandle);
            if (!WebPeeperModule.BlishHudInstance.Form.Focused)
            {
                GameService.GameIntegration.Gw2Instance.FocusGw2();
                Utils.SetForegroundWindow(WinHandle);
            }
            // IACE_DEFAULT = 0x0010
            Utils.ImmAssociateContextEx(WinHandle, 0x0, 0x0010);
            _himc = Utils.ImmGetContext(WinHandle);
            Utils.ImmSetOpenStatus(_himc, true);
            SetCompositionPostion();
        }
        public void Disable(bool tryFocusGame = true)
        {
            if (_himc == IntPtr.Zero) return;
            RestoreAllKeybinds();
            Utils.ImmSetOpenStatus(_himc, false);
            Utils.ImmReleaseContext(WinHandle, _himc);
            _himc = IntPtr.Zero;
            if (tryFocusGame && WebPeeperModule.BlishHudInstance.Form.Focused)
            {
                GameService.GameIntegration.Gw2Instance.FocusGw2();
            }
        }
    }
}