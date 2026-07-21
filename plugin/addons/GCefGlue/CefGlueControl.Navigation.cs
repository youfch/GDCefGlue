using System.Threading.Tasks;
using Godot;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Events;

namespace GDCefGlue
{
    public partial class CefGlueControl
    {
        public void GoBack() => _browser?.GoBack();
        public void GoForward() => _browser?.GoForward();
        public void NavigateToUrl(string url) { if (!string.IsNullOrEmpty(url)) _browser?.GetMainFrame()?.LoadUrl(url); }
        public void Reload(bool ignoreCache = false) => _browser?.Reload();

        public void ShowDeveloperTools()
        {
            var windowInfo = CefWindowInfo.Create();
            windowInfo.RuntimeStyle = CefRuntimeStyle.Chrome;
            _browserHost?.ShowDevTools(windowInfo, _client, new CefBrowserSettings(), new CefPoint());
        }

        public void CloseDeveloperTools() => _browserHost?.CloseDevTools();

        // ── CEF 事件回调 ──
        internal void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
        { if (!_disposed && frame.IsMain) CallDeferred(nameof(NotifyAddressChanged), url); }
        private void NotifyAddressChanged(string url) => AddressChanged?.Invoke(this, url);

        internal void OnTitleChange(CefBrowser browser, string title)
        { if (!_disposed) { Title = title; CallDeferred(nameof(NotifyTitleChanged), title); } }
        private void NotifyTitleChanged(string title) => TitleChanged?.Invoke(this, title);

        internal void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
        { if (!_disposed) CallDeferred(nameof(NotifyLoadStart)); }
        private void NotifyLoadStart() => LoadStart?.Invoke(this, new LoadStartEventArgs(null));

        internal void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
        {
            if (_disposed) return;
            if (_renderMode == RenderMode.EmbeddedWindow && frame.IsMain)
                InjectEventForwardingScriptIfNeeded();
            CallDeferred(nameof(NotifyLoadEnd));
        }
        private void NotifyLoadEnd() => LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0));

        internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        { if (!_disposed) CallDeferred(nameof(NotifyLoadError), errorText, failedUrl); }
        private void NotifyLoadError(string errorText, string failedUrl)
            => LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl));
    }
}