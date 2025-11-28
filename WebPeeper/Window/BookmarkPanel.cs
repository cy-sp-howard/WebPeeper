using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using CefSharp;
using CefSharp.OffScreen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BhModule.WebPeeper
{
    public class BookmarkPanel : Panel
    {
        static public BookmarkPanel Instance;
        static readonly Point _bgOverSize = new(75, 50);
        static readonly Texture2D _bgTexture = Content.GetTexture("controls/window/502049");
        static readonly Texture2D _editBtnTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("edit.png");
        static readonly Texture2D _addBtnTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("add.png");
        static readonly Texture2D _emptyTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("empty.png");
        static readonly Color _emptyColor = new(0x30303030);
        static readonly Regex _notSupportStringMatcher = new("[^A-z0-9\\s\\W]");
        const string _bookmarksJsonFileName = "bookmarks.json";
        readonly OrderedDictionary _bookmarkMenuItems = [];
        readonly IconButton _editBtn;
        readonly IconButton _addBtn;
        readonly Menu _menuContainer;
        readonly Glide.Tween _animFade;
        readonly string _jsonPath;
        Rectangle _bgDestRect = Rectangle.Empty;
        Rectangle _bgSourceRect = Rectangle.Empty;
        Rectangle _emptyDestRect = Rectangle.Empty;
        bool _editing = false;
        ChromiumWebBrowser WebBrowser => WebPeeperModule.Instance.CefService.WebBrowser;
        public BookmarkPanel()
        {
            Instance = this;
            Title = " ";
            CanScroll = true;
            Padding = new(0, _bgOverSize.X, _bgOverSize.Y, 0);

            _editBtn = new(_editBtnTexture, 20, 1.5f)
            {
                BasicTooltipText = "Edit Bookmarks"
            };
            _editBtn.Click += delegate { SetChildrenEditState(!_editing); };
            _addBtn = new(_addBtnTexture, 20, 1.5f)
            {
                BasicTooltipText = "Bookmark Current Page"
            };
            _addBtn.Click += delegate
            {
                if (!WebBrowser.CanExecuteJavascriptInMainFrame) return;
                var getTitleTask = WebBrowser.EvaluateScriptAsync("document.title");
                getTitleTask.Wait(TimeSpan.FromSeconds(1));
                var bookmarkName = getTitleTask.IsCanceled ? WebPeeperModule.Instance.UIService.BrowserWindow.Subtitle : (string)getTitleTask.Result.Result;
                AddBookmark(new()
                {
                    Name = _notSupportStringMatcher.Replace(bookmarkName, "-"),
                    URL = WebBrowser.Address
                });
            };
            _menuContainer = new BookmarkMenu()
            {
                Size = Size,
                Parent = this
            };
            Visible = false;
            Opacity = 0f;
            _animFade = Animation.Tweener.Tween(this, new { Opacity = 1f }, 0.2f).Repeat().Reflect();
            _animFade.Pause();
            _animFade.OnUpdate(() =>
            {
                _addBtn.Opacity = _opacity;
                _editBtn.Opacity = _opacity;
            });
            _animFade.OnComplete(() =>
            {
                _animFade.Pause();
                if (_opacity <= 0) Visible = false;
                else _menuContainer.Opacity = 1f;
            });

            _jsonPath = Path.Combine(CefService.CefSettingFolder, _bookmarksJsonFileName);
            if (!File.Exists(_jsonPath)) return;
            try
            {
                foreach (var bookmark in JsonConvert.DeserializeObject<Bookmark[]>(File.ReadAllText(_jsonPath)))
                {
                    _bookmarkMenuItems[bookmark] = CreateBookmarkItem(bookmark);
                }
            }
            finally { }
        }
        public override void Show()
        {
            if (Visible) return;
            Opacity = 0;
            Visible = true;
            _animFade.Resume();
        }
        public override void Hide()
        {
            if (!Visible) return;
            _menuContainer.Opacity = 0.5f;
            _animFade.Resume();
        }
        protected override void OnShown(EventArgs e)
        {
            ShowBtns();
            base.OnShown(e);
        }
        protected override void OnHidden(EventArgs e)
        {
            HideBtns();
            SetChildrenEditState(false);
            base.OnHidden(e);
        }
        protected override void OnMoved(MovedEventArgs e)
        {
            SetBtnsLocation();
            base.OnMoved(e);
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            _bgDestRect = new Rectangle(0, 0, Width + _bgOverSize.X, Height + _bgOverSize.Y);
            _bgSourceRect = new Rectangle(_bgTexture.Width - _bgDestRect.Width, _bgTexture.Height - _bgDestRect.Height, _bgDestRect.Width, _bgDestRect.Height);
            var emptyWidth = (int)(Size.X * 0.3);
            var emptyHeight = (double)_emptyTexture.Height / _emptyTexture.Width * emptyWidth;
            _emptyDestRect = new((Size.X - emptyWidth) / 2, (Size.Y - (int)emptyHeight) / 2, emptyWidth, (int)emptyHeight);
            if (_menuContainer is not null)
            {
                _menuContainer.Size = e.CurrentSize;
            }
            SetBtnsLocation();
            base.OnResized(e);
        }
        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawOnCtrl(this, _bgTexture, _bgDestRect, _bgSourceRect);
            if (_bookmarkMenuItems.Count == 0) spriteBatch.DrawOnCtrl(this, _emptyTexture, _emptyDestRect, _emptyColor);
            base.PaintBeforeChildren(spriteBatch, bounds);
        }
        protected override void DisposeControl()
        {
            _editBtn.Dispose();
            _addBtn.Dispose();
            base.DisposeControl();
        }
        public void SetChildrenEditState(bool edit)
        {
            if (_editing == edit) return;
            _editing = edit;

            foreach (var item in _bookmarkMenuItems.Values)
            {
                if (item is BookmarkItem bookmarkItem)
                {
                    bookmarkItem.SetEditState(edit);
                }
            }
            if (!_editing) WriteJson();
        }
        void ShowBtns()
        {
            _editBtn.Parent = Parent;
            _editBtn.ZIndex = ZIndex + 1;
            _editBtn.Visible = _bookmarkMenuItems.Count > 0;

            _addBtn.Parent = Parent;
            _addBtn.ZIndex = ZIndex + 1;
            _addBtn.Visible = true;
        }
        void SetBtnsLocation()
        {
            _editBtn.Location = new(Location.X + 10, Location.Y + 10);
            _addBtn.Location = new(Location.X + Width - 50, _editBtn.Location.Y);
        }
        void HideBtns()
        {
            _editBtn.Visible = false;
            _addBtn.Visible = false;
        }
        BookmarkItem CreateBookmarkItem(Bookmark bookmark)
        {
            BookmarkItem item = new(bookmark) { Parent = _menuContainer };
            item.Removed += delegate
            {
                _bookmarkMenuItems.Remove(bookmark);
                if (_bookmarkMenuItems.Count == 0)
                {
                    SetChildrenEditState(false);
                    _editBtn.Hide();
                }
            };
            item.Dragged += ReorderBookmarks;
            if (_editing) item.SetEditState(_editing);
            return item;
        }
        void ReorderBookmarks(object sender, BookmarkDraggedEventArgs e)
        {
            var itemIndex = -1;
            var insertIndex = -1;
            var lastIndex = _bookmarkMenuItems.Values.Count - 1;
            var targetKey = e.Bookmark;
            var target = _bookmarkMenuItems[e.Bookmark];
            var nextStartY = 0;
            var skipMatch = false;
            bool isInsertLast = false;
            foreach (var item in _bookmarkMenuItems.Values)
            {
                itemIndex++;
                insertIndex++;
                if (item is BookmarkItem bookmarkItem)
                {
                    var startY = nextStartY;
                    // wrong value if self, nextStartY wrong too due to moving
                    var endY = bookmarkItem.Top + bookmarkItem.Height / 2;
                    nextStartY = endY;
                    // skip add insertIndex, because remove self lead to index - 1 
                    // no move needed if matches self or next (except index last 2), no worries about wrong range
                    if (bookmarkItem.Equals(target)) { insertIndex--; skipMatch = itemIndex != lastIndex - 1; }
                    else if (skipMatch) skipMatch = false;
                    else if (e.Position >= startY && e.Position <= endY || (isInsertLast = (itemIndex == lastIndex && e.Position >= nextStartY)))
                    {
                        if (isInsertLast) insertIndex++;
                        _bookmarkMenuItems.Remove(targetKey);
                        _bookmarkMenuItems.Insert(insertIndex, targetKey, target);
                        break;
                    }
                }
            }

            foreach (var item in _bookmarkMenuItems.Values)
            {
                if (item is BookmarkItem bookmarkItem)
                {
                    bookmarkItem.Parent = null;
                }
            }
            foreach (var item in _bookmarkMenuItems.Values)
            {
                if (item is BookmarkItem bookmarkItem)
                {
                    bookmarkItem.Parent = _menuContainer;
                }
            }
        }
        void AddBookmark(Bookmark bookmark)
        {
            _bookmarkMenuItems[bookmark] = CreateBookmarkItem(bookmark);
            _editBtn.Show();
            WriteJson();
        }
        void WriteJson()
        {
            File.WriteAllText(_jsonPath, JsonConvert.SerializeObject(_bookmarkMenuItems.Keys));
        }
    }
    public class BookmarkMenu : Menu
    {
        static readonly FieldInfo _childPropertyChangedField = typeof(Control).GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        const int ItemHeight = 40;
        public bool ChildrenSeparated { get; private set; } = false;
        public BookmarkMenu()
        {
            MenuItemHeight = ItemHeight;
        }
        public void SeparateChildren()
        {
            foreach (var child in _children)
            {
                if (_childPropertyChangedField.GetValue(child) is PropertyChangedEventHandler)
                {
                    _childPropertyChangedField.SetValue(child, null);
                }
            }
            ChildrenSeparated = true;
        }
        protected override void OnChildRemoved(ChildChangedEventArgs e)
        {
            // before child truely removed
            if (ChildrenSeparated && _children.Count <= 1)
            {
                ChildrenSeparated = false;
            }
            base.OnChildRemoved(e);
        }
        public override void RecalculateLayout()
        {
            if (ChildrenSeparated) return;
            base.RecalculateLayout();
        }
    }
    public class BookmarkItem : MenuItem
    {
        readonly Bookmark _bookmark;
        public event EventHandler<EventArgs> Removed;
        public event EventHandler<BookmarkDraggedEventArgs> Dragged;
        readonly IconButton _removeBtn;
        readonly IconButton _sortBtn;
        readonly TextBox _nameInput;
        static readonly Texture2D _removeTexture = GameService.Content.GetTexture("common/733270");
        static readonly Texture2D _sortTexture = WebPeeperModule.Instance.ContentsManager.GetTexture("sort.png");
        bool _editing = false;
        int _changeOrderAbsoluteStartPointY;
        int _changeOrderStartPointY;
        int _maxChangeOrderY;
        public BookmarkItem(Bookmark bookmark) : base(bookmark.Name)
        {
            _bookmark = bookmark;
            _sortBtn = new(_sortTexture, 25, 3, 1.3f) { Visible = false, BasicTooltipText = "Drag to reorder" };
            _sortBtn.LeftMouseButtonPressed += StartChangeOrder;
            _removeBtn = new(_removeTexture, 25, 1.5f) { Visible = false };
            _removeBtn.Click += delegate
            {
                Removed?.Invoke(this, EventArgs.Empty);
                Dispose();
            };
            _nameInput = new TextBox() { Visible = false, Font = Content.DefaultFont16 };
            _nameInput.InputFocusChanged += (sender, e) =>
            {
                if (!e.Value)
                {
                    _bookmark.Name = string.IsNullOrWhiteSpace(_nameInput.Text) ? "-" : _nameInput.Text;
                    _nameInput.SelectionStart = _nameInput.Text.Length;
                    _nameInput.SelectionEnd = _nameInput.Text.Length;
                    return;
                }
                _nameInput.SelectionStart = 0;
                _nameInput.SelectionEnd = _nameInput.Text.Length;
            };
        }
        public override void UpdateContainer(GameTime t)
        {
            if (_sortBtn?.Parent is null)
            {
                _sortBtn.Parent = Parent.Parent;
            }
            if (_removeBtn?.Parent is null)
            {
                _removeBtn.Parent = Parent.Parent;
            }
            if (_nameInput?.Parent is null)
            {
                _nameInput.Parent = Parent.Parent;
            }
            SetEditControlBounds();
        }
        protected override void OnMoved(MovedEventArgs e)
        {
            SetEditControlBounds();
            base.OnMoved(e);
        }
        protected override void OnResized(ResizedEventArgs e)
        {
            SetEditControlBounds();
            base.OnResized(e);
        }
        protected override void OnClick(MouseEventArgs e)
        {
            if (!_editing)
            {
                WebPeeperModule.Instance.CefService.WebBrowser.Load(_bookmark.URL);
                Parent.Parent.Hide();
            }
            base.OnClick(e);
        }
        void StartChangeOrder(object sender, EventArgs e)
        {
            _changeOrderAbsoluteStartPointY = AbsoluteBounds.Location.Y;
            _changeOrderStartPointY = Location.Y;
            GameService.Input.Mouse.LeftMouseButtonPressed += StopChangeOrder;
            GameService.Input.Mouse.LeftMouseButtonReleased += StopChangeOrder;
            GameService.Input.Mouse.MouseMoved += OnChangingOrder;
            if (Parent is BookmarkMenu menu) menu.SeparateChildren();
            _maxChangeOrderY = Parent.Children.Last().Bottom;
        }
        void StopChangeOrder(object sender, EventArgs e)
        {
            GameService.Input.Mouse.LeftMouseButtonPressed -= StopChangeOrder;
            GameService.Input.Mouse.LeftMouseButtonReleased -= StopChangeOrder;
            GameService.Input.Mouse.MouseMoved -= OnChangingOrder;
            if (Parent is BookmarkMenu menu && menu.ChildrenSeparated)
            {
                Dragged?.Invoke(this, new(_bookmark, Top));
            }
        }
        void OnChangingOrder(object sender, MouseEventArgs e)
        {
            var moved = e.MousePosition.Y - _changeOrderAbsoluteStartPointY;
            var topValue = _changeOrderStartPointY + moved;
            if (topValue > _maxChangeOrderY) topValue = _maxChangeOrderY;
            else if (topValue < 0) topValue = 0;
            Top = topValue;
        }
        void SetEditControlBounds()
        {
            if (_sortBtn?.Parent is null || _removeBtn?.Parent is null || _nameInput?.Parent is null || !_editing) return;
            _sortBtn.Location = new Point(0, Location.Y + Height / 2 - _sortBtn.Height / 2);
            _nameInput.Location = new Point(_sortBtn.Right, Location.Y + Height / 2 - _nameInput.Height / 2);
            _nameInput.Width = Width - 70 - _nameInput.Location.X;
            _removeBtn.Location = new Point(Width - 50, Location.Y + Height / 2 - _removeBtn.Height / 2);
        }
        public void SetEditState(bool val)
        {
            _editing = val;
            if (_editing)
            {
                SetEditControlBounds();
                _nameInput.Text = _text;
                _text = "";
            }
            else
            {
                _text = _bookmark.Name;
            }
            _sortBtn.Visible = _editing;
            _nameInput.Visible = _editing;
            _removeBtn.Visible = _editing;
        }
        protected override void DisposeControl()
        {
            StopChangeOrder(this, new());
            _sortBtn.Dispose();
            _nameInput.Dispose();
            _removeBtn.Dispose();
            Removed = null;
            Dragged = null;
            base.DisposeControl();
        }
    }
    public class Bookmark
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name;
        [JsonProperty("url", Required = Required.Always)]
        public string URL;
    }
    public class BookmarkDraggedEventArgs(Bookmark bookmark, int position) : EventArgs
    {
        readonly public Bookmark Bookmark = bookmark;
        readonly public int Position = position;
    }
}
