using System;
using System.Runtime.InteropServices;

namespace GDCefGlue
{
    internal static class NativeWindowMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        internal const uint SWP_NOZORDER = 0x0004;
        internal const uint SWP_NOACTIVATE = 0x0010;
        internal static readonly IntPtr HWND_TOP = IntPtr.Zero;
    }
}