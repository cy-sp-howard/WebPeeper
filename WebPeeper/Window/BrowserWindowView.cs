using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using CefSharp;
using CefSharp.OffScreen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    public class BrowserWindowView : View
    {
        Container _window;
        WindowContent _windowContent;
        protected override void Build(Container window)
        {
            _window = window;
            var createTask = WebPeeperModule.Instance.CefService.CreateWebBrowser();
            var done = createTask.Wait(TimeSpan.FromSeconds(30));
            if (!done) return;
            // bring to same thread , because sometime Tween Initialize lerperSet before lerperSet add
            WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
            {
                _windowContent = new WindowContent()
                {
                    Parent = window,
                    Size = _window.ContentRegion.Size
                };
                _window.Resized += OnWindowResize;
            });
        }
        void OnWindowResize(object sender, ResizedEventArgs evt)
        {
            _windowContent.Size = _window.ContentRegion.Size;
        }
        protected override void Unload()
        {
            _window.Resized -= OnWindowResize;
            _windowContent?.Dispose();
        }
    }
    public class NavigationBar : FlowPanel
    {
        static public NavigationBar Instance;
        static readonly Texture2D _btnTexture = GameService.Content.GetTexture("784268");
        static readonly Texture2D _bookmarkBtnTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("bookmark.png");
        IconButton _backBtn;
        IconButton _fowardBtn;
        TextBox _addressInput;
        LoadingSpinner _loading;

        public event EventHandler<EventArgs> BookmarkBtnClicked;
        ChromiumWebBrowser WebBrowser => WebPeeperModule.Instance.CefService.WebBrowser;
        public NavigationBar()
        {
            Instance = this;
            Height = 30;
            SetChildren();
            if (WebBrowser is null) return;
            if (WebBrowser.CanExecuteJavascriptInMainFrame)
            {
                WebBrowser.EvaluateScriptAsync("document.fullscreen").ContinueWith(t =>
                {
                    Visible = !(bool)t.Result.Result;
                });
            }
            WebBrowser.LoadingStateChanged += HandleLoading;
            WebBrowser.AddressChanged += HandleAddress;
        }
        public void SetAddressInputText(string text)
        {
            if (_addressInput is null) return;
            _addressInput.Text = text;
        }
        void SetChildren()
        {
            _backBtn = new IconButton(_btnTexture, Height, 2)
            {
                Parent = this,
                Visible = WebBrowser?.CanGoBack ?? false
            };
            _backBtn.Click += delegate { WebBrowser?.Back(); };
            _fowardBtn = new IconButton(_btnTexture, Height, 2)
            {
                Parent = this,
                Visible = WebBrowser?.CanGoForward ?? false,
                IconRotation = MathHelper.ToRadians(180f)
            };
            _fowardBtn.Click += delegate { WebBrowser?.Forward(); };

            var bookmarkBtn = new IconButton(_bookmarkBtnTexture, Height, 2) { Parent = this };
            bookmarkBtn.Click += delegate
            {
                BookmarkBtnClicked?.Invoke(this, EventArgs.Empty);
            };

            _addressInput = new TextBox()
            {
                Font = GameService.Content.DefaultFont14,
                Height = Height,
                Parent = this
            };
            _addressInput.EnterPressed += delegate
            {
                if (string.IsNullOrWhiteSpace(_addressInput.Text))
                {
                    _addressInput.Text = WebBrowser?.Address ?? "";
                    return;
                }
                HandleLoading(this, new(null, true, false, true));
                WebPeeperModule.Instance.CefService.LastAddressInputText = _addressInput.Text;
                var cts = new CancellationTokenSource();
                void stopManuallyErrTrigger(object sender, LoadingStateChangedEventArgs e)
                {
                    WebBrowser.LoadingStateChanged -= stopManuallyErrTrigger;
                    cts.Cancel();
                }
                if (WebBrowser is not null) WebBrowser.LoadingStateChanged += stopManuallyErrTrigger;
                WebBrowser?.LoadUrlAsync(WebPeeperModule.Instance.CefService.LastAddressInputText);
                Task.Delay(1000, cts.Token).ContinueWith(t =>
                {
                    if (WebBrowser is not null) WebBrowser.LoadingStateChanged -= stopManuallyErrTrigger;
                    if (t.IsCanceled || t.IsFaulted) return;
                    HandleLoading(this, new(null, true, false, false));
                    WebPeeperModule.Instance.CefService.OnUrlLoadError(this, new(null, null, CefErrorCode.InvalidUrl, "", _addressInput.Text));
                });
            };
            _addressInput.InputFocusChanged += (sender, e) =>
            {
                _addressInput.SelectionStart = !e.Value ? _addressInput.Text.Length : 0;
                _addressInput.SelectionEnd = _addressInput.Text.Length;
                if (!e.Value && _addressInput.Text.Length == 0) _addressInput.Text = WebBrowser?.Address ?? "";
            };
            _addressInput.Text = WebBrowser?.Address ?? "";

            _loading = new LoadingSpinner()
            {
                Visible = WebBrowser?.IsLoading ?? false,
                Enabled = false,
                Parent = this,
                Size = new(_addressInput.Height, _addressInput.Height)
            };
        }
        public override void RecalculateLayout()
        {
            base.RecalculateLayout();
            RecalculatetLoadingLocation();
        }
        void HandleLoading(object sender, LoadingStateChangedEventArgs e)
        {
            if (_fowardBtn.Visible != e.CanGoForward || _backBtn.Visible != e.CanGoBack)
            {
                _backBtn.Visible = e.CanGoBack;
                _fowardBtn.Visible = e.CanGoForward;
                RecalculateLayout();
                RecalculateAddressInputWidth();
            }
            _loading.Visible = e.IsLoading;
            RecalculatetLoadingLocation();
        }
        void RecalculatetLoadingLocation()
        {
            if (_loading is null) return;
            _loading.Location = new Point(Width - _loading.Width, 0);
        }
        void HandleAddress(object sender, AddressChangedEventArgs e)
        {
            _addressInput.Text = e.Address;
        }
        void RecalculateAddressInputWidth()
        {
            if (_addressInput is null) return;
            var inputX = 0;
            foreach (var c in Children)
            {
                if (!c.Visible) continue;
                if (c == _addressInput) break;
                inputX = c.Right;
            }
            _addressInput.Width = Width - inputX - 1;
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            RecalculateAddressInputWidth();
            base.OnResized(e);
        }
        protected override void DisposeControl()
        {
            if (WebBrowser is not null)
            {
                WebBrowser.LoadingStateChanged -= HandleLoading;
                WebBrowser.AddressChanged -= HandleAddress;
            }
            BookmarkBtnClicked = null;
        }
    }
    public class WindowContent : FlowPanel
    {
        NavigationBar _navigationBar;
        WebPainter _webPainter;
        BookmarkPanel _bookmarkPanel;
        public WindowContent()
        {
            ControlPadding = new Vector2(0, 10);
            FlowDirection = ControlFlowDirection.SingleTopToBottom;
            SetChildren();
        }
        void SetChildren()
        {
            _navigationBar = new NavigationBar()
            {
                Parent = this,
            };
            _navigationBar.BookmarkBtnClicked += delegate
            {
                if (_bookmarkPanel.Visible) _bookmarkPanel.Hide();
                else _bookmarkPanel.Show();
            };
            _navigationBar.Hidden += OnNavigationBarVisibleChanged;
            _navigationBar.Shown += OnNavigationBarVisibleChanged;
            _webPainter = new WebPainter()
            {
                Parent = this,
            };
            _webPainter.Click += delegate
            {
                if (!_bookmarkPanel.Visible) return;
                _bookmarkPanel.Hide();
            };
            _bookmarkPanel = new BookmarkPanel() { Parent = this };
            _bookmarkPanel.Shown += delegate { _webPainter.Disabled = true; };
            _bookmarkPanel.Hidden += delegate { _webPainter.Disabled = false; };
        }
        public override void RecalculateLayout()
        {
            base.RecalculateLayout();
            if (_bookmarkPanel is null) return;
            _bookmarkPanel.Location = _webPainter.Location;
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            ResetNavigationBarSize();
            ResetWebPainterSize();
            ResetBookmarkSize();
            base.OnResized(e);
        }
        void ResetNavigationBarSize()
        {
            _navigationBar.Width = Width;
        }
        void ResetWebPainterSize()
        {
            if (_navigationBar.Visible)
            {
                _webPainter.Size = new(Size.X - 1, Size.Y - _navigationBar.Height - 1 - (int)ControlPadding.Y);
            }
            else
            {
                _webPainter.Size = new(Size.X - 1, Size.Y - 1);
            }
        }
        void ResetBookmarkSize()
        {
            _bookmarkPanel.Height = MathHelper.Min(_webPainter.Height, 700);
            _bookmarkPanel.Width = MathHelper.Max(_webPainter.Width / 2, 150);
        }
        void OnNavigationBarVisibleChanged(object sender, EventArgs e)
        {
            ResetWebPainterSize();
            RecalculateLayout();
        }
    }
}
