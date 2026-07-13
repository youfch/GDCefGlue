using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

internal sealed class GodotPermissionHandler : CefPermissionHandler
{
    private readonly CefGlueControl _control;

    public GodotPermissionHandler(CefGlueControl control)
    {
        _control = control;
    }

    protected override bool OnRequestMediaAccessPermission(
        CefBrowser browser, CefFrame frame, string requestingOrigin,
        CefMediaAccessPermissionTypes requestedPermissions, CefMediaAccessCallback callback)
    {
        GD.Print($"[GodotPermissionHandler] OnRequestMediaAccessPermission: origin={requestingOrigin}, permissions={requestedPermissions}");

        if (!_control.EnableMediaStream)
        {
            callback.Continue(CefMediaAccessPermissionTypes.None);
            return true;
        }

        callback.Continue(requestedPermissions);
        return true;
    }

    protected override bool OnShowPermissionPrompt(
        CefBrowser browser, ulong promptId, string requestingOrigin,
        CefPermissionRequestTypes requestedPermissions, CefPermissionPromptCallback callback)
    {
        GD.Print($"[GodotPermissionHandler] OnShowPermissionPrompt: origin={requestingOrigin}, permissions={requestedPermissions}");
        callback.Continue(CefPermissionRequestResult.Deny);
        return true;
    }
}