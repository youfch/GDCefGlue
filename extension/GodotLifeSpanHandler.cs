using System;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

/// <summary>
/// Minimal CEF client for native popup windows. Returns null for all handlers,
/// letting CEF use its native default implementations (native context menu,
/// native cursor, native rendering, etc.). This avoids inheriting the parent
/// OSR browser's Godot-specific handlers.
/// </summary>
internal sealed class PopupCefClient : CefClient
{
    // No handler overrides — all return null → CEF uses native defaults.
}

internal class GodotLifeSpanHandler : CefLifeSpanHandler
{
    private readonly CefGlueControl _control;

    public GodotLifeSpanHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnAfterCreated(CefBrowser browser)
    {
        if (_control.IsDisposed) return;
        _control.OnBrowserCreated(browser);
    }

    protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
    {
        if (_control.IsDisposed) return true;
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

        if (_control.HasNewWindowSubscribers)
        {
            bool isNewWindow = targetDisposition == CefWindowOpenDisposition.NewWindow
                            || targetDisposition == CefWindowOpenDisposition.NewPopup;
            _control.RaiseNewWindowRequested(targetUrl, isNewWindow);
            return true;
        }

        // ── 无订阅者 → 让 CEF 创建原生弹窗 ──
        //
        // 1. 提供纯净的 PopupCefClient，避免继承父浏览器 OSR 的 GodotCefClient。
        //    父浏览器的 GodotCefClient 会返回自定义的 ContextMenuHandler（Godot PopupMenu），
        //    在原生弹窗中无法工作（需要 Godot 控件树）。PopupCefClient 所有 handler 返回 null，
        //    CEF 使用原生默认实现，包括原生右键菜单、原生光标等。
        //
        // 2. 将 windowInfo 设置为原生弹出窗口。父浏览器是 OSR（离屏渲染）模式时，
        //    CEF 默认的 windowInfo 可能也是离屏模式，导致新窗口的右键菜单无法工作
        //    （OSR 无 HWND，CEF 默认菜单需要 HWND）。
        client = new PopupCefClient();
        var hwnd = (IntPtr)DisplayServer.Singleton.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, 0);
        if (hwnd != IntPtr.Zero)
        {
            windowInfo.SetAsPopup(hwnd, targetFrameName ?? string.Empty);
        }
        return false;
    }
}
