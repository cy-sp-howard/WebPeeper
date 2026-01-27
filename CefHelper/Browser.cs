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
        static public bool CanGoBack => WebBrowser?.CanGoBack == true;
        static public bool CanGoForward => WebBrowser?.CanGoForward == true;
        static public string Address => string.IsNullOrWhiteSpace(WebBrowser?.Address) ? "" : WebBrowser.Address;
        static public bool IsLoading => WebBrowser?.IsLoading == true;
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
        static public dynamic WebBrowser { get; private set; }
        static public void CefSettingInit(string defaultUserAgent, string localesPath, string settingPath, string subprocessPath, bool clearUserData)
        {
            if (Cef.IsInitialized) return;
            CefSharpSettings.FocusedNodeChangedEnabled = true;
            var settings = new CefSettings();
            settings.EnableAudio();
            settings.UserAgent = defaultUserAgent;
            if (!string.IsNullOrEmpty(localesPath)) settings.LocalesDirPath = localesPath;
            settings.BrowserSubprocessPath = Path.Combine(subprocessPath, "CefSharp.BrowserSubprocess.exe");
            settings.CachePath = Path.Combine(settingPath, "CefCache");
            settings.UserDataPath = Path.Combine(settingPath, "CefUserData");
            settings.CefCommandLineArgs.Add("gpu-preferences"); // not sure what is it, but gw2 cefhost.exe uses it
            if (clearUserData)
            {
                Directory.Delete(settings.CachePath, true);
                Directory.Delete(settings.UserDataPath, true);
            }
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
        }
        static public Task<bool> Create(string defaultUrl, int frameRate)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (WebBrowser == null || WebBrowser.IsDisposed)
            {
                var browserSetting = new BrowserSettings(true)
                {
                    WindowlessFrameRate = frameRate
                };

                var webBrowser = new ChromiumWebBrowser(defaultUrl, browserSetting);
                WebBrowser = webBrowser;
                webBrowser.BrowserInitialized += delegate { tcs.TrySetResult(true); };
                webBrowser.LoadingStateChanged += (s, e) =>
                {
                    LoadingStateChanged?.Invoke(e.CanGoBack, e.CanGoForward, e.IsLoading);
                };
                webBrowser.AddressChanged += (s, e) => { AddressChanged?.Invoke(e.Address); };
                webBrowser.TitleChanged += (s, e) => { TitleChanged?.Invoke(e.Title); };
                webBrowser.FrameLoadStart += (s, e) => { FrameLoadStart?.Invoke(); };
                webBrowser.LoadError += (s, e) => { UrlLoadError?.Invoke(e.FailedUrl); };
                webBrowser.Paint += (s, e) =>
                {
                    e.Handled = Paint?.Invoke(e.BufferHandle, e.Width, e.Height) == true;
                };
                webBrowser.DisplayHandler = new FullscreenHandler()
                {
                    FullscreenModeChanged = (isFullscreen) => { FullscreenModeChanged?.Invoke(isFullscreen); },
                };
                webBrowser.FrameHandler = new WebUnloadHandler()
                {
                    MainFrameChanged = () => { FocusedChanged?.Invoke(false); }
                };
                webBrowser.RequestHandler = new WebFocusHandler();
                webBrowser.LifeSpanHandler = new PopupHandler();
                webBrowser.RenderProcessMessageHandler = new NodeFocusHandler()
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
                        if (string.IsNullOrWhiteSpace(ContextCreatedScript) || webBrowser?.CanExecuteJavascriptInMainFrame != true) return;
                        frame.ExecuteJavaScriptAsync(ContextCreatedScript);
                    }
                };
            }
            if (WebBrowser.IsBrowserInitialized) tcs.TrySetResult(true);
            return tcs.Task;
        }
        static public void Close()
        {
            WebBrowser?.Dispose();
            WebBrowser = null;
        }
        static public void Back()
        {
            WebBrowser?.Back();
        }
        static public void Forward()
        {
            WebBrowser?.Forward();
        }
        static public void LoadUrlAsync(string url)
        {
            WebBrowser?.LoadUrlAsync(url);
        }
        static public void WasHidden(bool hidden)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.WasHidden(hidden);
            }
        }
        static public void FocusBlurredElement()
        {
            if (WebBrowser?.CanExecuteJavascriptInMainFrame != true) return;
            WebBrowser.ExecuteScriptAsync("webPeeper_focusBlurredElement()");
        }
        static public void ImeCommitText(string text)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.ImeCommitText(text, new(int.MaxValue, int.MaxValue), 0);
            }
        }
        static public void ImeSetComposition(string text, int location, (int, int, uint, uint, bool)[] underlines)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.ImeSetComposition(
                text,
                [.. underlines.Select(i => new CompositionUnderline(new(i.Item1, i.Item2), i.Item3, i.Item4, i.Item5))],
                new(int.MaxValue, int.MaxValue),
                new(location, location));
            }
        }
        static public void ImeFinishComposingText()
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.ImeFinishComposingText(false);
            }
        }
        static public void SendCursorEvent(int x, int y, int scrollWheelValue, MouseEventType mouseEventType, int mouseEvtFlag, bool isUseTouch)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                var host = webBrowser?.GetBrowserHost();
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
                    }
                    else
                    {
                        MouseEvent mouseEvt = new(x, y, modifiers);
                        host.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, false, 0);
                    }
                }
                else if (mouseEventType == MouseEventType.RightMouseButtonPressed)
                {
                    host.SendFocusEvent(true);
                    MouseEvent mouseEvt = new(x, y, modifiers);
                    host.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, false, 0);
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
                    }
                    else
                    {
                        MouseEvent mouseEvt = new(x, y, modifiers);
                        host.SendMouseClickEvent(mouseEvt, MouseButtonType.Left, isLeftButtonPressed, 0);
                    }
                }
                else if (mouseEventType == MouseEventType.RightMouseButtonReleased)
                {
                    MouseEvent mouseEvt = new(x, y, modifiers);
                    host.SendMouseClickEvent(mouseEvt, MouseButtonType.Right, isRightButtonPressed, 0);
                }
            }
        }
        static public void SendKeyEvent(int modifiers, bool isKeyDown, int key)
        {
            var keyEvt = new KeyEvent
            {
                Modifiers = (CefEventFlags)modifiers,
                Type = isKeyDown ? KeyEventType.KeyDown : KeyEventType.KeyUp,
                WindowsKeyCode = key
            };
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.SendKeyEvent(keyEvt);
            }
        }
        static public void SendKeyEvent(int msg, IntPtr wParam64, IntPtr lParam64)
        {
            // Browser.GetBrowserHost().SendKeyEvent(_m.Msg, _m.WParam.CastToInt32(), _m.LParam.CastToInt32()); 
            // port from CefBrowserHostWrapper::SendKeyEvent , due to above throw System.InvalidProgramException: Invalid IL code in wine
            var wParam = wParam64.CastToInt32();
            var lParam = lParam64.CastToInt32();
            var evt = new KeyEvent()
            {
                Modifiers = (CefEventFlags)GetCefKeyboardModifiers(wParam, lParam),
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
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetBrowserHost()?.SendKeyEvent(evt);
            }
        }
        static int GetCefKeyboardModifiers(int wParam, int lParam)
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
            return (int)modifiers;
        }
        static public Task SetBrowserSize(int w, int h)
        {
            if (WebBrowser is null) return Task.FromResult(false);
            return WebBrowser.ResizeAsync(w, h);
        }
        static public void Zoom(float rate)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                webBrowser?.GetZoomLevelAsync()?.ContinueWith(t =>
                {
                    webBrowser.SetZoomLevel(t.Result + 0.2f * rate);
                });
            }
        }
        static public Task<bool> GetFullscreenState()
        {
            var tcs = new TaskCompletionSource<bool>();
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                if (webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult(false);

                webBrowser.EvaluateScriptAsync("document.fullscreen")?.ContinueWith(t =>
                {
                    tcs.TrySetResult((bool)t.Result.Result);
                });
                Task.Delay(2000).ContinueWith(t => { tcs.TrySetResult(false); });
            }
            else { Task.FromResult(false); }
            return tcs.Task;
        }
        static public Task<(int, int)> GetInputPosition()
        {
            var tcs = new TaskCompletionSource<(int, int)>();
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                if (webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult((0, 0));

                webBrowser.EvaluateScriptAsync("webPeeper_getFocusLocation()")?.ContinueWith(t =>
                {
                    var inputXY = (List<object>)t.Result.Result;
                    var inputX = (int)inputXY[0] + 2;
                    var inputY = (int)inputXY[1];
                    tcs.TrySetResult((inputX, inputY));
                });
                Task.Delay(2000).ContinueWith(t => { tcs.TrySetResult((0, 0)); });
            }
            else return Task.FromResult((0, 0));
            return tcs.Task;
        }
        static public Task<string> GetMainFrameTitle()
        {
            var tcs = new TaskCompletionSource<string>();
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                if (webBrowser?.CanExecuteJavascriptInMainFrame != true) return Task.FromResult("");

                webBrowser.EvaluateScriptAsync("document.title")?.ContinueWith(t =>
                {
                    tcs.TrySetResult((string)t.Result.Result);
                });
                Task.Delay(1000).ContinueWith(t => { tcs.TrySetResult(""); });

            }
            else { Task.FromResult(""); }
            return tcs.Task;
        }
        static public void BlurInput()
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                if (webBrowser?.CanExecuteJavascriptInMainFrame != true) return;
                webBrowser?.ExecuteScriptAsync("webPeeper_blur()");
            }
        }
        static public Task<byte[]> GetScreenshot()
        {
            if (WebBrowser is null) return Task.FromResult<byte[]>([]);
            return WebBrowser.CaptureScreenshotAsync();
        }
        static public void ApplyUserAgent(string agentString)
        {
            if (WebBrowser is ChromiumWebBrowser webBrowser)
            {
                using var devToolsClient = webBrowser.GetDevToolsClient();
                devToolsClient.Emulation.SetUserAgentOverrideAsync(agentString);
            }
        }
        static public void Dispose()
        {
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
