using Xilium.CefGlue;

namespace GDCefGlueExtension;

/// <summary>
/// CEF 焦点处理器，桥接 CEF 内部焦点变化与 Godot 侧焦点状态同步。
/// </summary>
internal class GodotFocusHandler : CefFocusHandler
{
    private readonly CefGlueControl _control;

    public GodotFocusHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnGotFocus(CefBrowser browser)
    {
    }

    protected override bool OnSetFocus(CefBrowser browser, CefFocusSource source)
    {
        return false;
    }

    protected override void OnTakeFocus(CefBrowser browser, bool next)
    {
        if (_control.IsDisposed) return;
        _control.OnCefTakeFocus();
    }
}