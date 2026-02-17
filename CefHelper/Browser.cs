using Blish_HUD;
using Blish_HUD.Input;
using CefSharp;
using CefSharp.Enums;
using CefSharp.Internals;
using CefSharp.OffScreen;
using CefSharp.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CefHelper
{
    static public class Browser
    {
        const string SchemeName = "blish-hud";
        const string DomainName = "web-peeper";
        const int ExecuteScriptTimeout = 1000;
        static public bool CanGoBack => _webBrowser?.CanGoBack == true;
        static public bool CanGoForward => _webBrowser?.CanGoForward == true;
        static public string Address => string.IsNullOrWhiteSpace(_webBrowser?.Address) ? "" : _webBrowser.Address;
        static public bool IsLoading => _webBrowser?.IsLoading == true;
        static public string ContextCreatedScript = "";
        static public event Func<string, Stream> BlishHudSchemeRequested;
        static public event Action<bool> FocusedChanged;
        static public event Action<string> TitleChanged;
        static public event Action FrameLoadStart;
        static public event Action<string> UrlLoadError;
        static public event Action<bool> FullscreenModeChanged;
        static public event Action<bool, bool, bool> LoadingStateChanged;
        static public event Action<string> AddressChanged;
        static public event Func<IntPtr, int, int, bool> Paint;
        static ChromiumWebBrowser _webBrowser;
        static internal readonly Logger Logger = Logger.GetLogger(typeof(Browser));
        static public void CefSettingInit(string localesPath, string cachePath, string subprocessPath)
        {
            if (Cef.IsInitialized == true) return;
            CefSharpSettings.FocusedNodeChangedEnabled = true;
            var settings = new CefSettings();
            settings.EnableAudio();
            if (!string.IsNullOrEmpty(localesPath)) settings.LocalesDirPath = localesPath;
            settings.BrowserSubprocessPath = Path.Combine(subprocessPath, "CefSharp.BrowserSubprocess.exe");
            settings.CachePath = cachePath;
#if !GREATER_THAN_114
            // deleted after 114, https://github.com/cefsharp/CefSharp/issues/4518
            settings.UserDataPath = settings.CachePath;
#endif
            settings.CefCommandLineArgs.Add("gpu-preferences"); // not sure what is it, but gw2 cefhost.exe uses it
            settings.PersistSessionCookies = true;
            settings.RegisterScheme(new CefCustomScheme()
            {
                SchemeName = SchemeName,
                DomainName = DomainName,
                SchemeHandlerFactory = new CefSchemeHandlerFactory()
                {
                    BlishHudSchemeRequested = (request) =>
                    {
                        var uri = new Uri(request.Url);
                        var filePath = uri.AbsolutePath.Remove(0, 1);
                        return (BlishHudSchemeRequested?.Invoke(filePath), Cef.GetMimeType(Path.GetExtension(filePath)));
                    }
                },
            });
            Cef.Initialize(settings);
            Logger.Debug("CefSettingInit");
        }
        static public Task<bool> Create(string defaultUrl, int frameRate, bool isMobile)
        {
            Logger.Debug("Create: resolve if browser initialized.");
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                if (_webBrowser is null || _webBrowser.IsDisposed)
                {
                    Logger.Debug("Create: create browser");
                    _webBrowser = new ChromiumWebBrowser(defaultUrl);
                    _webBrowser.BrowserInitialized += delegate
                    {
                        SetFrameRate(frameRate);
                        tcs.TrySetResult(true);
                    };
                    _webBrowser.LoadingStateChanged += (s, e) =>
                    {
                        LoadingStateChanged?.Invoke(e.CanGoBack, e.CanGoForward, e.IsLoading);
                    };
                    _webBrowser.AddressChanged += (s, e) => { AddressChanged?.Invoke(e.Address); };
                    _webBrowser.TitleChanged += (s, e) => { TitleChanged?.Invoke(e.Title); };
                    _webBrowser.FrameLoadStart += (s, e) => { FrameLoadStart?.Invoke(); };
                    _webBrowser.LoadError += (s, e) => { UrlLoadError?.Invoke(e.FailedUrl); };
                    _webBrowser.Paint += (s, e) =>
                    {
                        e.Handled = Paint?.Invoke(e.BufferHandle, e.Width, e.Height) == true;
                    };
                    _webBrowser.DisplayHandler = new FullscreenHandler()
                    {
                        FullscreenModeChanged = (isFullscreen) => { FullscreenModeChanged?.Invoke(isFullscreen); },
                    };
                    _webBrowser.FrameHandler = new WebUnloadHandler()
                    {
                        MainFrameChanged = () => { FocusedChanged?.Invoke(false); }
                    };
                    _webBrowser.RequestHandler = new RequestHandler() { IsMobile = isMobile };
                    _webBrowser.LifeSpanHandler = new PopupHandler();
                    _webBrowser.RenderProcessMessageHandler = new NodeFocusHandler()
                    {
                        FocusNodeChanged = (node) =>
                        {
                            if (node is null) { FocusedChanged?.Invoke(false); return; }
                            bool isContenteditable = node["contenteditable"] is not null && node["contenteditable"] != "false";
                            if (isContenteditable || node.TagName == "INPUT" || node.TagName == "TEXTAREA")
                            {
                                FocusedChanged?.Invoke(true);
                            }
                            else FocusedChanged?.Invoke(false);
                        },
                        ContextCreated = (frame) =>
                        {
                            if (string.IsNullOrWhiteSpace(ContextCreatedScript) || _webBrowser.CanExecuteJavascriptInMainFrame != true) return;
                            frame.ExecuteJavaScriptAsync(ContextCreatedScript);
                        }
                    };
                }
                if (_webBrowser.IsBrowserInitialized) tcs.TrySetResult(true);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }
        static public void Close()
        {
            _webBrowser?.Dispose();
            _webBrowser = null;
            Logger.Debug("Close: close browser");
        }
        static public void Back()
        {
            _webBrowser?.Back();
        }
        static public void Forward()
        {
            _webBrowser?.Forward();
        }
        static public void LoadUrlAsync(string url)
        {
            _webBrowser?.LoadUrlAsync(url);
        }
        static public void WasHidden(bool hidden)
        {
            _webBrowser?.GetBrowserHost()?.WasHidden(hidden);
            Logger.Debug($"WasHidden: hidden = {hidden}");
        }
        static public void FocusBlurredElement()
        {
            if (_webBrowser?.CanExecuteJavascriptInMainFrame != true) return;
            _webBrowser.ExecuteScriptAsync("webPeeper_focusBlurredElement()");
            Logger.Debug("FocusBlurredElement");
        }
        static public void ImeCommitText(string text)
        {
            _webBrowser?.GetBrowserHost()?.ImeCommitText(text, new(int.MaxValue, int.MaxValue), 0);
            Logger.Debug($"ImeCommitText: {text}");
        }
        static public void ImeSetComposition(string text, int location, (int, int, uint, uint, bool)[] underlines)
        {
            _webBrowser?.GetBrowserHost()?.ImeSetComposition(
            text,
            [.. underlines.Select(i => new CompositionUnderline(new(i.Item1, i.Item2), i.Item3, i.Item4, i.Item5))],
            new(int.MaxValue, int.MaxValue),
            new(location, location));
            Logger.Debug($"ImeSetComposition: {text}");
        }
        static public void ImeFinishComposingText()
        {
            _webBrowser?.GetBrowserHost()?.ImeFinishComposingText(false);
            Logger.Debug("ImeFinishComposingText");
        }
        static public void SendCursorEvent(int x, int y, int scrollWheelValue, MouseEventType mouseEventType, int mouseEvtFlag, bool isUseTouch)
        {
            var host = _webBrowser?.GetBrowserHost();
            if (host is null) return;
            var modifiers = (CefEventFlags)mouseEvtFlag;
            var isLeftButtonPressed = modifiers.HasFlag(CefEventFlags.LeftMouseButton);
            var isRightButtonPressed = modifiers.HasFlag(CefEventFlags.RightMouseButton);
            if (mouseEventType == MouseEventType.MouseMoved)
            {
                if (isUseTouch && isLeftButtonPressed)
                {
                    CefSharp.Structs.TouchEvent touchEvt = new()
                    {
                        Id = 1,
                        X = (float)x,
                        Y = (float)y,
                        PointerType = PointerType.Touch,
                        Pressure = 1,
                        Type = TouchEventType.Moved,
                        Modifiers = modifiers,
                    };
                    host.SendTouchEvent(touchEvt);
                }
                else
                {
                    MouseEvent mouseEvt = new(x, y, modifiers);
                    host.SendMouseMoveEvent(mouseEvt, false);
                }
            }
            else if (mouseEventType == MouseEventType.MouseWheelScrolled)
            {
                MouseEvent mouseEvt = new(x, y, modifiers);
                var wheelValue = scrollWheelValue < -1000 ? 240 : scrollWheelValue;
                if (modifiers.HasFlag(CefEventFlags.ControlDown) && !modifiers.HasFlag(CefEventFlags.AltDown) && !modifiers.HasFlag(CefEventFlags.ShiftDown))
                {
                    Zoom(wheelValue > 0 ? 1 : -1);
                }
                else
                {
                    host.SendMouseWheelEvent(mouseEvt, 0, wheelValue);
                }
            }
            else if (mouseEventType == MouseEventType.LeftMouseButtonPressed)
            {
                host.SendFocusEvent(true);
                if (isUseTouch)
                {
                    TouchEvent touchEvt = new()
                    {
                        Id = 1,
                        X = x,
                        Y = y,
                        PointerType = PointerType.Touch,
                        Pressure = 1,
                        Type = TouchEventType.Pressed,
                        Modifiers = modifiers,
                    };
                    host.SendTouchEvent(touchEvt);
                    Logger.Debug("SendCursorEvent: left touch pressed");
                }
                else
                {
                    MouseEvent mouseEvt = new(x, y, modifiers);
                    host.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, false, 0);
                    Logger.Debug("SendCursorEvent: left mouse pressed");
                }
            }
            else if (mouseEventType == MouseEventType.RightMouseButtonPressed)
            {
                host.SendFocusEvent(true);
                MouseEvent mouseEvt = new(x, y, modifiers);
                host.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, false, 0);
                Logger.Debug("SendCursorEvent: right mouse pressed");
            }
            else if (mouseEventType == MouseEventType.LeftMouseButtonReleased)
            {
                if (isUseTouch)
                {
                    TouchEvent touchEvt = new()
                    {
                        Id = 1,
                        X = x,
                        Y = y,
                        PointerType = PointerType.Touch,
                        Pressure = 1,
                        Type = TouchEventType.Released,
                        Modifiers = modifiers,
                    };
                    host.SendTouchEvent(touchEvt);
                    Logger.Debug("SendCursorEvent: left touch released");
                }
                else
                {
                    MouseEvent mouseEvt = new(x, y, modifiers);
                    host.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, isLeftButtonPressed, 0);
                    Logger.Debug("SendCursorEvent: left mouse released");
                }
            }
            else if (mouseEventType == MouseEventType.RightMouseButtonReleased)
            {
                MouseEvent mouseEvt = new(x, y, modifiers);
                host.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, isRightButtonPressed, 0);
                Logger.Debug("SendCursorEvent: right mouse released");
            }
        }
        static public void SendKeyEvent(int modifiers, bool isKeyDown, int key)
        {
            var evt = new KeyEvent
            {
                Modifiers = (CefEventFlags)modifiers,
                Type = isKeyDown ? KeyEventType.KeyDown : KeyEventType.KeyUp,
                WindowsKeyCode = key
            };
            _webBrowser?.GetBrowserHost()?.SendKeyEvent(evt);
            Logger.Debug($"SendKeyEvent(key): Modifiers = {evt.Modifiers}, Type = {evt.Type}, WindowsKeyCode = {evt.WindowsKeyCode} .");

        }
        static public void SendKeyEvent(int msg, IntPtr wParam64, IntPtr lParam64)
        {
            // Browser.GetBrowserHost().SendKeyEvent(_m.Msg, _m.WParam.CastToInt32(), _m.LParam.CastToInt32()); 
            // port from CefBrowserHostWrapper::SendKeyEvent , due to above throw System.InvalidProgramException: Invalid IL code in wine
            var wParam = wParam64.CastToInt32();
            var lParam = lParam64.CastToInt32();
            var evt = new KeyEvent()
            {
                Modifiers = GetCefKeyboardModifiers(wParam, lParam),
                WindowsKeyCode = wParam,
                NativeKeyCode = lParam,
                IsSystemKey = false,
                Type = (WM)msg switch
                {
                    WM.KEYDOWN => KeyEventType.KeyDown,
                    WM.KEYUP => KeyEventType.KeyUp,
                    WM.CHAR => KeyEventType.Char,
                    _ => (KeyEventType)(-1),
                }
            };
            _webBrowser?.GetBrowserHost()?.SendKeyEvent(evt);
            Logger.Debug($"SendKeyEvent(msg): Modifiers = {evt.Modifiers}, Type = {evt.Type}, WindowsKeyCode = {evt.WindowsKeyCode},NativeKeyCode = {evt.NativeKeyCode}");
        }
        static CefEventFlags GetCefKeyboardModifiers(int wParam, int lParam)
        {
            CefEventFlags modifiers = 0;
            if (Native.IsKeyDown(VK.SHIFT)) modifiers |= CefEventFlags.ShiftDown;
            if (Native.IsKeyDown(VK.CONTROL)) modifiers |= CefEventFlags.ControlDown;
            if (Native.IsKeyDown(VK.MENU)) modifiers |= CefEventFlags.AltDown;

            if ((Native.GetKeyState(VK.NUMLOCK) & 1) != 0) modifiers |= CefEventFlags.NumLockOn;
            if ((Native.GetKeyState(VK.CAPITAL) & 1) != 0) modifiers |= CefEventFlags.CapsLockOn;

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
                    if (Native.IsKeyDown(VK.LSHIFT)) modifiers |= CefEventFlags.IsLeft;
                    else if (Native.IsKeyDown(VK.RSHIFT)) modifiers |= CefEventFlags.IsRight;
                    break;
                case VK.CONTROL:
                    if (Native.IsKeyDown(VK.LCONTROL)) modifiers |= CefEventFlags.IsLeft;
                    else if (Native.IsKeyDown(VK.RCONTROL)) modifiers |= CefEventFlags.IsRight;
                    break;
                case VK.MENU:
                    if (Native.IsKeyDown(VK.LMENU)) modifiers |= CefEventFlags.IsLeft;
                    else if (Native.IsKeyDown(VK.RMENU)) modifiers |= CefEventFlags.IsRight;
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
        static public void Repaint()
        {
            if (_webBrowser is null) return;
            _webBrowser.GetBrowserHost()?.Invalidate(PaintElementType.View);
        }
        static public void SetSize(int w, int h)
        {
            if (_webBrowser is null) return;
            _webBrowser.Size = new(w, h);
            Repaint();
        }
        static public void SetFrameRate(int val)
        {
            if (_webBrowser is null) return;
            var host = _webBrowser.GetBrowserHost();
            if (host is null) return;
            host.WindowlessFrameRate = val;
        }
        static public void Zoom(float rate)
        {
            _webBrowser?.GetZoomLevelAsync()?.ContinueWith(t =>
            {
                _webBrowser?.SetZoomLevel(t.Result + 0.2f * rate);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
        static public Task<bool> GetFullscreenState()
        {
            if (_webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult(false);
            var tcs = new TaskCompletionSource<bool>();
            _webBrowser.EvaluateScriptAsync("document.fullscreen")?.ContinueWith(t =>
                {
                    tcs.TrySetResult((bool)t.Result.Result);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
            Task.Delay(ExecuteScriptTimeout).ContinueWith(t => { tcs.TrySetResult(false); });
            return tcs.Task;
        }
        static public Task<(int, int)> GetInputPosition()
        {
            if (_webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult((0, 0));
            var tcs = new TaskCompletionSource<(int, int)>();
            _webBrowser.EvaluateScriptAsync("webPeeper_getFocusLocation()")?.ContinueWith(t =>
                {
                    var inputXY = (List<object>)t.Result.Result;
                    var inputX = (int)inputXY[0] + 2;
                    var inputY = (int)inputXY[1];
                    tcs.TrySetResult((inputX, inputY));
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
            Task.Delay(ExecuteScriptTimeout).ContinueWith(t => { tcs.TrySetResult((0, 0)); });
            return tcs.Task;
        }
        static public Task<string> GetMainFrameTitle()
        {
            if (_webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult("");
            var tcs = new TaskCompletionSource<string>();
            _webBrowser.EvaluateScriptAsync("document.title")?.ContinueWith(t =>
            {
                tcs.TrySetResult((string)t.Result.Result);
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            Task.Delay(ExecuteScriptTimeout).ContinueWith(t => { tcs.TrySetResult(""); });
            return tcs.Task;
        }
        static public void BlurInput()
        {
            Logger.Debug("BlurInput");
            if (_webBrowser?.CanExecuteJavascriptInMainFrame != true) return;
            _webBrowser.ExecuteScriptAsync("webPeeper_blur()");
        }
        static public Task<byte[]> GetScreenshot()
        {
            if (_webBrowser is null) return Task.FromResult<byte[]>([]);
            return _webBrowser.CaptureScreenshotAsync();
        }
        static public void SetMobileUserAgent(bool mobile)
        {
            if (_webBrowser?.RequestHandler is RequestHandler handler)
            {
                handler.IsMobile = mobile;
            }
        }
        static public void Dispose()
        {
            Logger.Debug("Dispose");
            Close();
            BlishHudSchemeRequested = null;
            FocusedChanged = null;
            TitleChanged = null;
            FrameLoadStart = null;
            UrlLoadError = null;
            FullscreenModeChanged = null;
            LoadingStateChanged = null;
            AddressChanged = null;
        }
    }
}
