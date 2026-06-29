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
        _control.OnAddressChange(browser, frame, url);
    }

    protected override void OnTitleChange(CefBrowser browser, string title)
    {
        _control.OnTitleChange(browser, title);
    }

    protected override bool OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
    {
        _control.OnCursorChanged(type);
        return _control.SyncCursor;
    }
}
