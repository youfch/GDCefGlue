using Xilium.CefGlue;

namespace GDCefGlueExtension;

/// <summary>
/// Handles context menu events from CEF.
/// Delegates all callbacks to <see cref="CefGlueControl"/> via thread-safe
/// <c>CallDeferred</c> marshalling (CEF UI thread -> Godot main thread).
/// </summary>
/// <remarks>
/// <para>OSR mode only. In EmbeddedWindow mode, <see cref="GodotCefClient"/>
/// returns <c>null</c> from <c>GetContextMenuHandler</c> so CEF's native
/// window menu is used.</para>
/// </remarks>
internal sealed class GodotContextMenuHandler : CefContextMenuHandler
{
    private readonly CefGlueControl _control;

    public GodotContextMenuHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnBeforeContextMenu(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams state,
        CefMenuModel model)
    {
        _control.OnBeforeContextMenu(browser, frame, state, model);
    }

    protected override bool RunContextMenu(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams parameters,
        CefMenuModel model,
        CefRunContextMenuCallback callback)
    {
        return _control.OnRunContextMenu(browser, frame, parameters, model, callback);
    }

    protected override bool OnContextMenuCommand(
        CefBrowser browser,
        CefFrame frame,
        CefContextMenuParams state,
        int commandId,
        CefEventFlags eventFlags)
    {
        return _control.OnContextMenuCommand(browser, frame, state, commandId, eventFlags);
    }

    protected override void OnContextMenuDismissed(
        CefBrowser browser,
        CefFrame frame)
    {
        _control.OnContextMenuDismissed(browser, frame);
    }
}