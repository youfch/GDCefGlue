using System;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal class GodotDisplayHandler : CefDisplayHandler
{
    private readonly CefGlueControl _control;

    public GodotDisplayHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
    {
        if (_control.IsDisposed) return;
        _control.OnAddressChange(browser, frame, url);
    }

    protected override void OnTitleChange(CefBrowser browser, string title)
    {
        if (_control.IsDisposed) return;
        _control.OnTitleChange(browser, title);
    }

    protected override bool OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
    {
        if (_control.IsDisposed) return false;
        _control.OnCursorChanged(type);
        if (_control._renderMode == GDCefGlueExtension.RenderMode.EmbeddedWindow) return false;
        return _control.SyncCursor;
    }
}
