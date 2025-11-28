using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;
using System;
using System.IO;

namespace CefHelper
{
    static public class Default
    {
        const string SchemeName = "blish-hud";
        const string DomainName = "web-peeper";
        static public void SetCefSchemeHandler(CefSettings settings, Func<IRequest, (Stream, string)> blishHudSchemeRequested)
        {
            settings.RegisterScheme(new CefCustomScheme()
            {
                SchemeName = SchemeName,
                DomainName = DomainName,
                SchemeHandlerFactory = new CefSchemeHandlerFactory() { BlishHudSchemeRequested = blishHudSchemeRequested },
            });
        }
        static public void SetBrowserHandlers(
            IWebBrowser browser,
            Action<IFrame> contextCreatedAction,
            Action<IDomNode> focusNodeChangedAction,
            Action mainFrameChangedAction,
            Action<bool> fullscreenModeChangeAction
            )
        {
            browser.DisplayHandler = new DdddHandler()
            {
                FullscreenModeChange = fullscreenModeChangeAction,
            };
            browser.FrameHandler = new WebUnloadHandler()
            {
                MainFrameChanged = mainFrameChangedAction
            };
            browser.RequestHandler = new WebFocusHandler();
            browser.LifeSpanHandler = new PopupHandler();
            browser.RenderProcessMessageHandler = new NodeFocusHandler()
            {
                FocusNodeChanged = focusNodeChangedAction,
                ContextCreated = contextCreatedAction,
            };
        }
    }
    public class CefSchemeHandlerFactory : ISchemeHandlerFactory
    {
        public Func<IRequest, (Stream, string)> BlishHudSchemeRequested;
        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            if (BlishHudSchemeRequested is null) return null;
            var (stream, mimeTtype) = BlishHudSchemeRequested(request);
            if (stream is null) return null;
            return ResourceHandler.FromStream(stream, mimeTtype, true);
        }
    }
    class DdddHandler : DisplayHandler
    {
        public Action<bool> FullscreenModeChange;
        protected override void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
        {
            FullscreenModeChange?.Invoke(fullscreen);
        }
    }
    class WebFocusHandler : RequestHandler
    {
        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            chromiumWebBrowser.GetBrowserHost().SendFocusEvent(true);
            return base.OnBeforeBrowse(chromiumWebBrowser, browser, frame, request, userGesture, isRedirect);
        }
    }
    class WebUnloadHandler : FrameHandler
    {
        public Action MainFrameChanged;
        protected override void OnMainFrameChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame oldFrame, IFrame newFrame)
        {
            MainFrameChanged?.Invoke();
        }
    }
    class NodeFocusHandler : IRenderProcessMessageHandler
    {
        public Action<IDomNode> FocusNodeChanged;
        public Action<IFrame> ContextCreated;
        void IRenderProcessMessageHandler.OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
            FocusNodeChanged?.Invoke(node);
        }
        void IRenderProcessMessageHandler.OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            // after BindObject
            ContextCreated?.Invoke(frame);
        }
        void IRenderProcessMessageHandler.OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            // will trigger blur
        }
        void IRenderProcessMessageHandler.OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
        {
        }
    }
    class PopupHandler : LifeSpanHandler
    {
        protected override bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            chromiumWebBrowser.Load(targetUrl);
            newBrowser = null;
            return true;
        }
    }
}
