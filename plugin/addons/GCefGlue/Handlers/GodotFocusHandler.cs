using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF 焦点处理器，桥接 CEF 内部焦点变化与 Godot 侧焦点状态同步。
    /// CEF 的默认 OnSetFocus/OnGotFocus/OnTakeFocus 均为空实现，
    /// 此 handler 在关键入口点同步 Godot 侧状态。
    /// </summary>
    internal class GodotFocusHandler : CefFocusHandler
    {
        private readonly CefGlueControl _control;

        public GodotFocusHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called when the browser component has received focus.
        /// 浏览器内 input 获得焦点时触发，Godot 侧无需额外操作（聚焦由 _GuiInput 中的 GrabFocus 驱动）。
        /// </summary>
        protected override void OnGotFocus(CefBrowser browser)
        {
        }

        /// <summary>
        /// Called when CEF requests focus. Return false to allow CEF to set focus normally.
        /// </summary>
        protected override bool OnSetFocus(CefBrowser browser, CefFocusSource source)
        {
            // 允许 CEF 正常设置焦点
            return false;
        }

        /// <summary>
        /// Called when the browser component is about to lose focus (e.g., user tabs out of browser).
        /// 同步 Godot 侧焦点状态，如关闭 IME。
        /// </summary>
        protected override void OnTakeFocus(CefBrowser browser, bool next)
        {
            _control.OnCefTakeFocus();
        }
    }
}