using System;
using System.Runtime.InteropServices;

namespace GDCefGlue
{
    // ── 窗口嵌入模式 Native P/Invoke ───────────────────────────────────
    // 与 godot_wry 一样，Win32 API 调用集中隔离，不与业务逻辑混在一起。
    internal static class NativeWindowMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter,
            string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        internal const int GWL_STYLE = -16;
        internal const int GWL_EXSTYLE = -20;
        internal const uint WS_CLIPCHILDREN = 0x02000000;
        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal const uint SWP_SHOWWINDOW = 0x0040;
        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    }
}