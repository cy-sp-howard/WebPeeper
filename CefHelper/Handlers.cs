using CefSharp;
using CefSharp.Handler;
using CefSharp.Structs;
using Microsoft.Xna.Framework.Audio;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace CefHelper
{
    class CefSchemeHandlerFactory : ISchemeHandlerFactory
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
    class RequestHandler : CefSharp.Handler.RequestHandler
    {
        public bool IsMobile = false;
        public string MobileUserAgent = "";
        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            chromiumWebBrowser.GetBrowserHost().SendFocusEvent(true);
            return base.OnBeforeBrowse(chromiumWebBrowser, browser, frame, request, userGesture, isRedirect);
        }
        protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
        {
            return new UserAgentHandler(IsMobile);
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
    class UserAgentHandler(bool isMobile) : ResourceRequestHandler
    {
        protected override CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback)
        {
            if (chromiumWebBrowser.RequestHandler is RequestHandler handler)
            {
                var mobileUserAgent = handler.MobileUserAgent;
                if (string.IsNullOrWhiteSpace(mobileUserAgent))
                {
                    var platformRegex = new Regex("\\(Windows[^(]*\\)", RegexOptions.IgnoreCase);
                    var originUserAgent = request.GetHeaderByName("User-Agent");
                    mobileUserAgent = platformRegex.Replace(originUserAgent, "(Linux; Android 10; K)");
                    var browserRegex = new Regex("Chrome\\/[\\d.]+", RegexOptions.IgnoreCase);
                    var match = browserRegex.Match(mobileUserAgent);
                    mobileUserAgent = mobileUserAgent.Replace(match.Value, $"{match.Value} Mobile");
                    handler.MobileUserAgent = mobileUserAgent;
                }
                if (isMobile && !string.IsNullOrWhiteSpace(mobileUserAgent))
                {
                    request.SetHeaderByName("User-Agent", mobileUserAgent, true);
                }
            }
            return base.OnBeforeResourceLoad(chromiumWebBrowser, browser, frame, request, callback);
        }
    }
    class VolumeHandler() : AudioHandler
    {
        float _defaultVolume = 1;
        Process _process;
        DynamicSoundEffectInstance _dynamicSound;
        byte[] _channelMemoryData;
        int _channelCount;
        int _channelMemorySize;
        int _framesFloatMemorySize;
        byte[] _framesFloatMemoryData;
        int _frameCount;
        short[] _frames;
        byte[] _framesMemoryData;
        protected override bool GetAudioParameters(IWebBrowser chromiumWebBrowser, IBrowser browser, ref AudioParameters parameters)
        {
            return true;
        }
        protected override void OnAudioStreamStarted(IWebBrowser chromiumWebBrowser, IBrowser browser, AudioParameters parameters, int channels)
        {
            _process ??= Process.GetCurrentProcess();

            var isMonoChannel = channels == 1;
            _channelCount = isMonoChannel ? 1 : 2;
            _channelMemorySize = sizeof(Int64) * _channelCount;
            _channelMemoryData = new byte[_channelMemorySize];

            _frameCount = parameters.FramesPerBuffer;
            var _frameDataSize = _frameCount * _channelCount;
            _frames = new short[_frameDataSize];
            _framesMemoryData = new byte[sizeof(short) * _frameDataSize];
            _framesFloatMemorySize = sizeof(float) * _frameCount;
            _framesFloatMemoryData = new byte[_framesFloatMemorySize];

            _dynamicSound = new DynamicSoundEffectInstance(parameters.SampleRate, isMonoChannel ? AudioChannels.Mono : AudioChannels.Stereo);
            SetVolume(_defaultVolume);
            _dynamicSound.Play();
        }
        protected override void OnAudioStreamStopped(IWebBrowser chromiumWebBrowser, IBrowser browser)
        {
            _dynamicSound?.Dispose();
            _dynamicSound = null;
        }
        protected override void OnAudioStreamPacket(IWebBrowser chromiumWebBrowser, IBrowser browser, IntPtr data, int noOfFrames, long pts)
        {
            Native.ReadProcessMemory(_process.Handle, data, _channelMemoryData, _channelMemorySize, out _);
            var frames = _frames.AsSpan();

            for (int channelIndex = 0; channelIndex < _channelCount; channelIndex++)
            {
                IntPtr channel = new(BitConverter.ToInt64(_channelMemoryData, channelIndex * sizeof(Int64)));
                Native.ReadProcessMemory(_process.Handle, channel, _framesFloatMemoryData, _framesFloatMemorySize, out _);

                for (int frameIndex = 0; frameIndex < _frameCount; frameIndex++)
                {
                    float sample = BitConverter.ToSingle(_framesFloatMemoryData, frameIndex * sizeof(float));
                    frames[frameIndex * _channelCount + channelIndex] = (short)(sample * short.MaxValue);
                }
            }
            MemoryMarshal.AsBytes(frames).CopyTo(_framesMemoryData);
            _dynamicSound.SubmitBuffer(_framesMemoryData);
        }
        public void SetVolume(float val)
        {
            _defaultVolume = val;
            if (_dynamicSound is null) return;
            _dynamicSound.Volume = val;
        }
        protected override void Dispose(bool disposing)
        {
            _dynamicSound?.Dispose();
            _process?.Dispose();
            base.Dispose(disposing);
        }
    }
}
