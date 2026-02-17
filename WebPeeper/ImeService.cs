using Blish_HUD;
using Blish_HUD.Input;
using CefHelper;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace BhModule.WebPeeper
{
    // https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.nativewindow?view=windowsdesktop-10.0
    // https://github.com/cefsharp/CefSharp/blob/v143.0.90/CefSharp.Wpf/Internals/IMEHandler.cs
    internal class ImeService : NativeWindow
    {
        readonly IntPtr _winHandle;
        IntPtr _himc;
        Message _m;
        object _keyStateChangedCloned;
        object _keyReleasedCloned;
        object _keyPressedCloned;
        bool _mouseLeftPressed = false;
        Action _mouseLeftReleaseCallback;
        readonly Dictionary<string, (Action<object>, Func<object>)> _keybindsBackupMap = [];
        public ImeService()
        {
            _winHandle = WebPeeperModule.BlishHudInstance.FormHandle;
            AssignHandle(_winHandle);
            ActiveAutoBlur();
            GameService.Input.Mouse.LeftMouseButtonPressed += OnLeftMouseButtonPressed;
            GameService.Input.Mouse.LeftMouseButtonReleased += OnLeftMouseButtonReleased;
            _keybindsBackupMap.Add("KeyStateChanged", (v => { _keyStateChangedCloned = v; }, () => _keyStateChangedCloned));
            _keybindsBackupMap.Add("KeyReleased", (v => { _keyReleasedCloned = v; }, () => _keyReleasedCloned));
            _keybindsBackupMap.Add("KeyPressed", (v => { _keyPressedCloned = v; }, () => _keyPressedCloned));
        }
        public void Unload()
        {
            ReleaseHandle();
            WebPeeperModule.BlishHudInstance.Form.LostFocus -= OnHudLostFocus;
            GameService.Input.Mouse.LeftMouseButtonPressed -= OnLeftMouseButtonPressed;
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnLeftMouseButtonReleased;
        }
        protected override void WndProc(ref Message m)
        {
            if (WebPeeperModule.Instance.UiService?.BrowserWindow?.Visible == true && CefService.LibLoadStarted)
            {
                _m = m;
                if (HandleMsg()) return;
            }
            base.WndProc(ref m);
        }
        bool HandleMsg()
        {
            var handled = false;
            switch ((WM)_m.Msg)
            {
                case WM.KEYUP:
                case WM.KEYDOWN:
                case WM.CHAR:
                    Browser.SendKeyEvent(msg: _m.Msg, wParam64: _m.WParam, lParam64: _m.LParam);
                    handled = true;
                    break;
                case WM.IME_COMPOSITION:
                    SetCefComposition();
                    handled = true;
                    break;
                case WM.IME_ENDCOMPOSITION:
                    Browser.ImeSetComposition("", 0, []);
                    Browser.ImeFinishComposingText();
                    handled = true;
                    break;
                case WM.IME_STARTCOMPOSITION:
                    handled = true;
                    break;
            }
            _m = new();
            return handled;
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
            WebPeeperModule.Logger.Debug("ImeService.OnHudLostFocus: Blish.HUD lost focus, do BlurInput");
            Browser.BlurInput();
        }
        void SetCefComposition()
        {
            if (GetCompositionText(GCS.RESULTSTR, out string text))
            {
                Browser.ImeCommitText(text);
                Browser.ImeSetComposition(text, 0, []);
                Browser.ImeFinishComposingText();
            }
            else if (GetCompositionText(GCS.COMPSTR, out text))
            {
                GetCompositionSelection(text, out List<(int, int, uint, uint, bool)> attrs, out int caret);
                Browser.ImeSetComposition(text, caret, [.. attrs]);
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
        void SetCompositionPostion()
        {
            if (WebPainter.Instance is null) return;
            var x = WebPainter.Instance.LocationAtForm.X;
            var y = WebPainter.Instance.LocationAtForm.Y;

            Browser.GetInputPosition().ContinueWith(t =>
            {
                var (webX, webY) = t.Result;
                var offsetX = (int)(webX * GameService.Graphics.UIScaleMultiplier);
                var offsetY = (int)(webY * GameService.Graphics.UIScaleMultiplier);
                WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
                {
                    // CFS_POINT = 0x0002
                    Utils.ImmSetCompositionWindow(_himc, new CompositionForm() { Style = 0x0002, X = x + offsetX, Y = y + offsetY });
                });
            });
        }
        void GetCompositionSelection(string text, out List<(int, int, uint, uint, bool)> underlines, out int caretPosition)
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

                    bool thick = (from >= selectionStart && to <= selectionEnd);
                    underlines.Add(new(from, to, 0xFF000000, 0x00000000, thick));
                }
            }

            if (underlines.Count == 0)
            {
                var from = 0;
                var to = 0;
                bool thick = false;
                if (selectionStart > 0)
                {
                    to = selectionStart;
                }
                if (selectionEnd > selectionStart)
                {
                    from = selectionStart;
                    to = selectionEnd;
                    thick = true;
                }
                if (selectionEnd < text.Length)
                {
                    from = selectionEnd;
                    to = text.Length;
                }
                underlines.Add((from, to, 0xFF000000, 0x00000000, thick));
            }
        }
        void DisableAllKeybinds()
        {
            WebPeeperModule.Logger.Debug("ImeService.DisableAllKeybinds");
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
        void ActiveAutoBlur()
        {
            void active()
            {
                WebPeeperModule.BlishHudInstance.Form.LostFocus -= OnHudLostFocus;
                WebPeeperModule.BlishHudInstance.Form.LostFocus += OnHudLostFocus;
            }
            if (CefService.LibLoadStarted) active();
            else CefService.LibLoadStart += delegate { active(); };
        }
        void RestoreAllKeybinds()
        {
            WebPeeperModule.Logger.Debug("ImeService.RestoreAllKeybinds");
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
            WebPeeperModule.Logger.Debug("ImeService.Enable: enable typing");
            WebPeeperModule.Logger.Debug("ImeService.Enable: bring Blish.HUD to the foreground");
            Utils.SetForegroundWindow(_winHandle);
            if (!WebPeeperModule.BlishHudInstance.Window.IsForeground())
            {
                WebPeeperModule.Logger.Debug("ImeService.Enable: retry bring Blish.HUD to the foreground");
                GameService.GameIntegration.Gw2Instance.FocusGw2();
                Utils.SetForegroundWindow(_winHandle);
            }
            // IACE_DEFAULT = 0x0010
            Utils.ImmAssociateContextEx(_winHandle, 0x0, 0x0010);
            _himc = Utils.ImmGetContext(_winHandle);
            Utils.ImmSetOpenStatus(_himc, true);
            SetCompositionPostion();
        }
        public void Disable(bool tryFocusGame = true)
        {
            if (_himc == IntPtr.Zero) return;
            RestoreAllKeybinds();
            WebPeeperModule.Logger.Debug("ImeService.Disable: disable typing");
            Utils.ImmSetOpenStatus(_himc, false);
            Utils.ImmReleaseContext(_winHandle, _himc);
            _himc = IntPtr.Zero;
            if (tryFocusGame && WebPeeperModule.BlishHudInstance.Window.IsForeground())
            {
                GameService.GameIntegration.Gw2Instance.FocusGw2();
            }
        }
    }
}
