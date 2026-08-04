using System;
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
        public void NavigateToUrl(string url) { if (!string.IsNullOrEmpty(url)) { using var frame = _browser?.GetMainFrame(); frame?.LoadUrl(url); } }
        public void Reload(bool ignoreCache = false) => _browser?.Reload();

        public void ShowDeveloperTools()
        {
            var windowInfo = CefWindowInfo.Create();
windowInfo.RuntimeStyle = CefRuntimeStyle.Chrome;
            windowInfo.SetAsPopup(IntPtr.Zero, "DevTools");
            // 使用 PopupCefClient（空 handler），避免主浏览器 GodotCefClient 的
            // OSR 渲染处理器、事件转发器等干扰 DevTools 窗口。
            _browserHost?.ShowDevTools(windowInfo, new PopupCefClient(), new CefBrowserSettings(), new CefPoint());
        }

        public void CloseDeveloperTools() => _browserHost?.CloseDevTools();

        // ── 页面内查找 ──

        /// <summary>
        /// 开始或继续页面内搜索。
        /// </summary>
        /// <param name="searchText">搜索文本（空字符串隐式停止搜索）</param>
        /// <param name="forward">true=向前搜索，false=向后</param>
        /// <param name="matchCase">true=区分大小写</param>
        /// <param name="findNext">false=新查询（文本/matchCase 变化时自动重启），true=查找下一个</param>
        public void Find(string searchText, bool forward = true, bool matchCase = false, bool findNext = false)
            => _browserHost?.Find(searchText, forward, matchCase, findNext);

        /// <summary>
        /// 停止当前搜索。
        /// </summary>
        /// <param name="clearSelection">true=清除高亮，false=保持当前选中</param>
        public void StopFinding(bool clearSelection = true)
            => _browserHost?.StopFinding(clearSelection);

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
            if (frame.IsMain)
            {
                // OSR 模式注入输入焦点监听 JS（驱动 IME 激活/关闭）
                // EmbeddedWindow 模式 CEF 有真实 HWND，IME 由 OS 直接管理，不需要 Godot 介入
                if (_renderMode == RenderMode.OSR)
                {
                    InjectFocusWatcherIfNeeded();
                    InjectCaretTrackerIfNeeded();
                }
                // 嵌入窗口模式下注入事件转发 JS
                if (_renderMode == RenderMode.EmbeddedWindow)
                    InjectEventForwardingScriptIfNeeded();
            }
            CallDeferred(nameof(NotifyLoadEnd));
        }
        private void NotifyLoadEnd() => LoadEnd?.Invoke(this, new LoadEndEventArgs(null, 0));

        internal void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl)
        { if (!_disposed) CallDeferred(nameof(NotifyLoadError), errorText, failedUrl); }
        private void NotifyLoadError(string errorText, string failedUrl)
            => LoadError?.Invoke(this, new LoadErrorEventArgs(null, CefErrorCode.None, errorText, failedUrl));

        // ── 页面内查找回调 ──

        /// <summary>
        /// CEF UI 线程入口 — 从 GodotFindHandler.OnFindResult 调用。
        /// Marshal 到 Godot 主线程后触发 FindResult 事件。
        /// </summary>
        internal void OnFindResult(CefBrowser browser, int identifier, int count,
            CefRectangle selectionRect, int activeMatchOrdinal, bool finalUpdate)
        {
            if (_disposed) return;
            // int/bool 是 Variant 兼容类型，可直接通过 CallDeferred 传递
            // selectionRect 暂不传递（UI 只需 count/ordinal 显示 "N / M"）
            CallDeferred(nameof(NotifyFindResult), identifier, count, activeMatchOrdinal, finalUpdate);
        }

        private void NotifyFindResult(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
            => RaiseFindResult(identifier, count, activeMatchOrdinal, finalUpdate);
    }
}