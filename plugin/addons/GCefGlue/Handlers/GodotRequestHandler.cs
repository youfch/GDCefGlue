using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF Request Handler.
    ///
    /// 历史的 JS→C# iframe 桥接（godot://bridge 协议）已移除。
    /// JS↔C# 通信统一走 V8 IPC（RegisterJavascriptObject）与二进制通道
    /// （见 CefGlueControl.Bridge.cs）。
    /// </summary>
    internal sealed class GodotRequestHandler : CefRequestHandler
    {
        private readonly CefGlueControl _control;

        public GodotRequestHandler(CefGlueControl control)
        {
            _control = control;
        }

        protected override bool OnBeforeBrowse(
            CefBrowser browser, CefFrame frame, CefRequest request,
            bool userGesture, bool isRedirect)
        {
            // 不再拦截任何自定义协议导航。
            return false;
        }

        /// <summary>
        /// Return null to use default resource handling for all requests.
        /// </summary>
        protected override CefResourceRequestHandler GetResourceRequestHandler(
            CefBrowser browser, CefFrame frame, CefRequest request,
            bool isNavigation, bool isDownload,
            string requestInitiator, ref bool disableDefaultHandling)
        {
            return null;
        }
    }
}