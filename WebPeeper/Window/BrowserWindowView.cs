using BhModule.WebPeeper.Window;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using CefHelper;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    internal class BrowserWindowView : View
    {
        Container _window;
        Control _windowContent;
        protected override void Build(Container window)
        {
            _window = window;
            _window.ContentResized += OnWindowResize;
            if (CefService.Outdated)
            {
                ShowOutdatedWarning();
            }
            else ShowDownloadProgress();
        }
        void OnWindowResize(object sender, RegionChangedEventArgs evt)
        {
            if (_windowContent is null) return;
            _windowContent.Size = evt.CurrentRegion.Size;
        }
        protected override void Unload()
        {
            _window.ContentResized -= OnWindowResize;
            _windowContent?.Dispose();
        }
        void ShowOutdatedWarning()
        {
            if (Warning.IsAccepted) ShowDownloadProgress();
            else
            {
                _ = WebPeeperModule.Instance.DownloadService.Download(CefService.CurrentVersion);
                var warning = new Warning()
                {
                    Parent = _window,
                    Size = _window.ContentRegion.Size
                };
                warning.Accepted += (s, e) => { ShowDownloadProgress(); };
                _windowContent = warning;
            }
        }
        void ShowDownloadProgress()
        {
            var downloadService = WebPeeperModule.Instance.DownloadService;
            var downloaded = downloadService.CheckCefLib(CefService.CurrentVersion); // cant get correct Downloading state, due to async Download so check here
            if (!downloaded) _ = downloadService.Download(CefService.CurrentVersion);
            if (downloadService.Downloading || !downloaded)
            {
                var bar = new ProgressBar(() => downloadService.ProgressPercentage)
                {
                    Text = "Downloading CEF...",
                    Parent = _window,
                    Size = _window.ContentRegion.Size,
                    BarSize = new(150, 30)
                };
                bar.ProgressUpdated += (s, e) =>
                {
                    if (e.NewValue >= 1 && _window.Visible) ShowBrowser();
                };
                _windowContent = bar;
            }
            else ShowBrowser();
        }
        void ShowBrowser()
        {
            _windowContent?.Dispose();
            _windowContent = new WaitingCefSetup()
            {
                Parent = _window,
                Size = _window.ContentRegion.Size,
            };
            WebPeeperModule.Instance.CefService.StartBrowsing().ContinueWith(t =>
            {
                // bring to same thread , because sometime Tween Initialize lerperSet before lerperSet add
                WebPeeperModule.BlishHudInstance.Form.SafeInvoke(() =>
                {
                    _windowContent?.Dispose();
                    _windowContent = new WindowContent(_window.ContentRegion.Size)
                    {
                        Parent = _window
                    };
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }
    }
    internal class NavigationBar : FlowPanel
    {
        static readonly Texture2D _btnTexture = GameService.Content.GetTexture("784268");
        static readonly Texture2D _bookmarkBtnTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("bookmark.png");
        IconButton _backBtn;
        IconButton _fowardBtn;
        TextBox _addressInput;
        LoadingSpinner _loading;

        public event EventHandler<EventArgs> BookmarkBtnClicked;
        public NavigationBar()
        {
            Height = 30;
            SetChildren();
            Browser.GetFullscreenState().ContinueWith(t => { Visible = !t.Result; });
            Browser.LoadingStateChanged += HandleLoading;
            Browser.AddressChanged += HandleAddress;
            Browser.UrlLoadError += HandleAddress;
            Browser.FullscreenModeChanged += HandleFullscreen;
        }
        void SetChildren()
        {
            _backBtn = new IconButton(_btnTexture, Height, 2)
            {
                Parent = this,
                Visible = Browser.CanGoBack
            };
            _backBtn.Click += delegate { Browser.Back(); };
            _fowardBtn = new IconButton(_btnTexture, Height, 2)
            {
                Parent = this,
                Visible = Browser.CanGoForward,
                IconRotation = MathHelper.ToRadians(180f)
            };
            _fowardBtn.Click += delegate { Browser.Forward(); };

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
            _addressInput.InputFocusChanged += (s, e) =>
            {
                if (!e.Value) return;
                // WindowsClipboardService cant read cef copied
                var text = System.Windows.Forms.Clipboard.GetText();
                ClipboardUtil.WindowsClipboardService.SetTextAsync(text);
            };
            _addressInput.EnterPressed += delegate
            {
                if (string.IsNullOrWhiteSpace(_addressInput.Text))
                {
                    _addressInput.Text = Browser.Address;
                    return;
                }
                HandleLoading(true, false, true); // err url lead to hide loading too quick, so show it early
                WebPeeperModule.Instance.CefService.Search(_addressInput.Text);
            };
            _addressInput.InputFocusChanged += (sender, e) =>
            {
                _addressInput.SelectionStart = !e.Value ? _addressInput.Text.Length : 0;
                _addressInput.SelectionEnd = _addressInput.Text.Length;
                if (!e.Value && _addressInput.Text.Length == 0) _addressInput.Text = Browser.Address;
            };
            _addressInput.Text = Browser.Address;

            _loading = new LoadingSpinner()
            {
                Visible = Browser.IsLoading,
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
        void HandleLoading(bool canGoBack, bool canGoForward, bool isLoading)
        {
            if (_fowardBtn.Visible != canGoForward || _backBtn.Visible != canGoBack)
            {
                _backBtn.Visible = canGoBack;
                _fowardBtn.Visible = canGoForward;
                RecalculateLayout();
                RecalculateAddressInputWidth();
            }
            _loading.Visible = isLoading;
            RecalculatetLoadingLocation();
        }
        void RecalculatetLoadingLocation()
        {
            if (_loading is null) return;
            _loading.Location = new Point(Width - _loading.Width, 0);
        }
        void HandleFullscreen(bool isFullscreen)
        {
            if (isFullscreen) Hide();
            else Show();
        }
        void HandleAddress(string address)
        {
            _addressInput.Text = address;
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

            Browser.LoadingStateChanged -= HandleLoading;
            Browser.AddressChanged -= HandleAddress;
            Browser.UrlLoadError -= HandleAddress;
            Browser.FullscreenModeChanged -= HandleFullscreen;

            BookmarkBtnClicked = null;
        }
    }
    internal class WindowContent : Panel
    {
        readonly NavigationBar _navigationBar;
        Control _mainContent;
        readonly BookmarkPanel _bookmarkPanel;
        const int _gap = 10;
        public WindowContent(Point size)
        {
            _size = size;
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
            CreateMainContent();
            _bookmarkPanel = new BookmarkPanel() { Parent = this };
            _bookmarkPanel.Shown += delegate { if (_mainContent is WebPainter wp) wp.Disabled = true; };
            _bookmarkPanel.Hidden += delegate { if (_mainContent is WebPainter wp) wp.Disabled = false; };
            OnResized(new(size, size)); // init children size
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            ResetNavigationBarRect();
            ResetMainContentRect();
            ResetBookmarkRect();
            base.OnResized(e);
        }
        void CreateMainContent()
        {
            _mainContent = new WebPainter
            {
                Parent = this
            };
            _mainContent.Click += delegate
            {
                if (!_bookmarkPanel.Visible) return;
                _bookmarkPanel.Hide();
            };
            if (_bookmarkPanel is not null)
            {
                _bookmarkPanel.Parent = null;
                _bookmarkPanel.Parent = this;
            }
        }
        void ResetNavigationBarRect()
        {
            _navigationBar.Location = Point.Zero;
            _navigationBar.Width = Width;
        }
        void ResetMainContentRect()
        {
            if (_navigationBar.Visible)
            {
                _mainContent.Location = new(0, _navigationBar.Bottom + _gap);
                _mainContent.Size = new(Size.X - 1, Size.Y - _navigationBar.Height - 1 - _gap);
            }
            else
            {
                _mainContent.Location = Point.Zero;
                _mainContent.Size = new(Size.X - 1, Size.Y - 1);
            }
        }
        void ResetBookmarkRect()
        {
            _bookmarkPanel.Location = _mainContent.Location;
            _bookmarkPanel.Size = new(MathHelper.Max(_mainContent.Width / 2, 150), MathHelper.Min(_mainContent.Height, 700));
        }
        void OnNavigationBarVisibleChanged(object sender, EventArgs e)
        {
            ResetMainContentRect();
        }
    }
    internal class WaitingCefSetup : Control
    {
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            LoadingSpinnerUtil.DrawLoadingSpinner(this, spriteBatch, new Rectangle(Size.X / 2 - 50, Size.Y / 2 - 50, 100, 100));
        }
    }
}
