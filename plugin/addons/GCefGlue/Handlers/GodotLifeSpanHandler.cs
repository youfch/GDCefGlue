using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles browser lifecycle events from CEF.
    /// Manages browser creation and popup window behavior.
    /// </summary>
    internal class GodotLifeSpanHandler : CefLifeSpanHandler
    {
        private readonly CefGlueControl _control;

        public GodotLifeSpanHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called after a browser has been created.
        /// </summary>
        protected override void OnAfterCreated(CefBrowser browser)
        {
            if (_control.IsDisposed) return;
            _control.OnBrowserCreated(browser);
        }

        /// <summary>
        /// Called before a popup window is created.
        /// Can redirect popups to the current browser based on settings.
        /// For allowed popups, prevents them from stealing keyboard focus.
        /// </summary>
        /// <returns>True to cancel the popup, false to allow it.</returns>
        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
        {
            if (_control.OpenPopupInCurrentBrowser)
            {
                switch (targetDisposition)
                {
                    case CefWindowOpenDisposition.NewBackgroundTab:
                    case CefWindowOpenDisposition.NewForegroundTab:
                    case CefWindowOpenDisposition.NewWindow:
                    case CefWindowOpenDisposition.NewPopup:
                        _control.CallDeferred("NavigateToUrl", targetUrl);
                        return true;
                }
            }

            // 新窗口/新标签请求 → 触发 NewWindowRequested 事件（由上层 UI 创建新标签）
            if (_control.HasNewWindowSubscribers)
            {
                bool isNewWindow = targetDisposition == CefWindowOpenDisposition.NewWindow
                                || targetDisposition == CefWindowOpenDisposition.NewPopup;
                _control.RaiseNewWindowRequested(targetUrl, isNewWindow);
                return true; // 有订阅者 → 取消默认弹窗，由订阅者处理
            }
            return false; // 无订阅者 → 让 CEF 创建原生窗口
        }
    }
}
