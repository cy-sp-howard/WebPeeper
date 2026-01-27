using CefSharp;
using CefSharp.OffScreen;

namespace CefHelper
{
    public class Wrapper
    {
        public ChromiumWebBrowser Browser;
        public void Create(string url, BrowserSettings settings)
        {
            Browser = new ChromiumWebBrowser(url, settings);
        }
    }
}
