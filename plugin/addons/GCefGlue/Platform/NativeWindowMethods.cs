using System;
using System.Runtime.InteropServices;

namespace GDCefGlue
{
    /// <summary>
    /// 跨平台原生窗口操作方法。运行时根据当前 OS 选择 Win32 或 X11 实现。
    /// </summary>
    internal static class NativeWindowMethods
    {
        /// <summary>
        /// 将键盘焦点设置到指定窗口。
        /// Win32: SetFocus  Linux: XSetInputFocus  macOS: makeFirstResponder
        /// </summary>
        internal static void SetPlatformFocus(IntPtr window)
        {
            if (OperatingSystem.IsLinux())
                X11Methods.SetInputFocus(window);
            else if (OperatingSystem.IsMacOS())
                MacMethods.MakeFirstResponder(window);
            else
                Win32SetFocus(window);
        }

        /// <summary>
        /// 移动并调整窗口位置和大小。
        /// Win32: SetWindowPos  Linux: XMoveResizeWindow  macOS: setFrame
        /// </summary>
        internal static void MovePlatformWindow(IntPtr window, int x, int y, int width, int height)
        {
            if (OperatingSystem.IsLinux())
                X11Methods.MoveResizeWindow(window, x, y, width, height);
            else if (OperatingSystem.IsMacOS())
                MacMethods.SetViewFrame(window, x, y, width, height);
            else
                Win32SetWindowPos(window, HWND_TOP, x, y, width, height,
                    SWP_NOZORDER | SWP_NOACTIVATE);
        }

        /// <summary>
        /// 显示或隐藏嵌入的 CEF 子窗口。
        /// </summary>
        internal static void SetPlatformWindowVisible(IntPtr window, bool visible)
        {
            if (window == IntPtr.Zero) return;
            if (OperatingSystem.IsLinux())
                X11Methods.SetWindowVisible(window, visible);
            else if (OperatingSystem.IsMacOS())
                MacMethods.SetViewVisible(window, visible);
            else
                Win32ShowWindow(window, visible ? SW_SHOW : SW_HIDE);
        }

        // ── Win32 实现 ──

        [DllImport("user32.dll", EntryPoint = "SetFocus", SetLastError = true)]
        private static extern IntPtr Win32SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
        private static extern bool Win32SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
        private static extern bool Win32ShowWindow(IntPtr hWnd, int nCmdShow);

        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const int SW_HIDE = 0;
        internal const int SW_SHOW = 5;
        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    }
}