using CefSharp;
using CefSharp.Handler;
using System;
using System.IO;

namespace CefHelper
{
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
    class FullscreenHandler : DisplayHandler
    {
        public Action<bool> FullscreenModeChanged;
        protected override void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen)
        {
            FullscreenModeChanged?.Invoke(fullscreen);
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
