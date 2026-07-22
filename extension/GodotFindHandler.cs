using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal sealed class GodotFindHandler : CefFindHandler
{
    private readonly CefGlueControl _control;

    public GodotFindHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override void OnFindResult(
        CefBrowser browser,
        int identifier,
        int count,
        CefRectangle selectionRect,
        int activeMatchOrdinal,
        bool finalUpdate)
    {
        _control.OnFindResult(browser, identifier, count, selectionRect, activeMatchOrdinal, finalUpdate);
    }
}