using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF 权限处理器 — 处理页面发起的媒体访问权限请求（麦克风、摄像头等）。
    /// 当页面调用 getUserMedia() 时触发 OnRequestMediaAccessPermission。
    /// </summary>
    internal sealed class GodotPermissionHandler : CefPermissionHandler
    {
        private readonly CefGlueControl _control;

        public GodotPermissionHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// 页面请求媒体访问权限时调用（如 getUserMedia）。
        /// 返回 true 表示已处理，需调用 callback 授予或拒绝权限。
        /// 返回 false 则走默认处理（Chrome 运行时显示权限 UI，Alloy 运行时拒绝）。
        /// 注意：如果传了 "--enable-media-stream" 命令行开关，此方法不会被调用。
        /// </summary>
        protected override bool OnRequestMediaAccessPermission(
            CefBrowser browser,
            CefFrame frame,
            string requestingOrigin,
            CefMediaAccessPermissionTypes requestedPermissions,
            CefMediaAccessCallback callback)
        {
            GD.Print($"[GodotPermissionHandler] OnRequestMediaAccessPermission: origin={requestingOrigin}, permissions={requestedPermissions}");

            if (!_control.EnableMediaStream)
            {
                // 未启用媒体流 → 拒绝
                callback.Continue(CefMediaAccessPermissionTypes.None);
                return true;
            }

            // 授予所有请求的权限（麦克风、摄像头等）
            callback.Continue(requestedPermissions);
            return true;
        }

        /// <summary>
        /// 页面显示权限提示时调用（如地理位置、剪贴板、通知等）。
        /// 返回 true 表示已处理，需调用 callback 继续/取消。
        /// 返回 false 则走默认处理。
        /// </summary>
        protected override bool OnShowPermissionPrompt(
            CefBrowser browser,
            ulong promptId,
            string requestingOrigin,
            CefPermissionRequestTypes requestedPermissions,
            CefPermissionPromptCallback callback)
        {
            GD.Print($"[GodotPermissionHandler] OnShowPermissionPrompt: origin={requestingOrigin}, permissions={requestedPermissions}");

            // 默认拒绝所有权限提示（可后续根据需求开放）
            callback.Continue(CefPermissionRequestResult.Deny);
            return true;
        }
    }
}