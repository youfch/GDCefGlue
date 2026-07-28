using System;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    // ── IME 状态跟踪 ──
    private void ActivateIme()
    {
        _imeWanted = true;
        var w = GetWindow();
        if (w != null && !_imeActive)
        {
            w.SetImeActive(true);
            _imeActive = true;
        }
    }

    private void DeactivateIme()
    {
        _imeWanted = false;
        var w = GetWindow();
        if (w != null && _imeActive)
        {
            w.SetImeActive(false);
            _imeActive = false;
        }
    }

    /// <summary>
    /// 由 GodotRenderHandler.OnImeCompositionRangeChanged 调用，更新 IME 候选窗位置。
    /// </summary>
    internal void OnCefImeCompositionChanged(bool hasComposition, int x, int y, int width, int height)
    {
        if (hasComposition)
        {
            UpdateImePosition(x, y, width, height);
        }
    }

    /// <summary>
    /// 由 GodotFocusWatcher（JS focusin/focusout 回调）调用。
    /// </summary>
    internal void OnInputFocusChanged(bool focused)
    {
        if (focused) CallDeferred("_activate_ime");
        else CallDeferred("_deactivate_ime");
    }

    private void _activate_ime() => ActivateIme();
    private void _deactivate_ime() => DeactivateIme();

    /// <summary>
    /// JS → C# 桥接对象：页面通过 window.__hostFocus.onInputFocusChanged(bool) 通知 C# 输入焦点变化。
    /// </summary>
    private sealed class GodotFocusWatcher
    {
        private readonly CefGlueControl _control;
        public GodotFocusWatcher(CefGlueControl control) => _control = control;
        public void OnInputFocusChanged(bool focused) => _control.OnInputFocusChanged(focused);
    }

    protected override void _GuiInput(InputEvent @event)
    {
        if (_browserHost == null || _renderMode == RenderMode.EmbeddedWindow) return;
        switch (@event)
        {
            case InputEventMouseMotion m: SendMouseMoveEvent(m); break;
            case InputEventMouseButton b: SendMouseButtonEvent(b); break;
            case InputEventKey k:
                SendKeyEvent(k);
                // 拦截导航键（Tab/Home/End/方向键）防止焦点从浏览器控件逃逸
                if (k.Pressed && !k.Echo)
                {
                    switch (k.Keycode)
                    {
                        case Key.Tab:
                        case Key.Home:
                        case Key.End:
                        case Key.Left:
                        case Key.Right:
                        case Key.Up:
                        case Key.Down:
                            GetViewport()?.SetInputAsHandled();
                            break;
                    }
                }
                break;
        }
    }

    private void SendMouseMoveEvent(InputEventMouseMotion e)
    {
        if (_browserHost == null) return;
        var p = GetLocalMousePosition(); var m = GetModifiers(e);
        if (_isMousePressed && _pressedButton != (CefMouseButtonType)(-1)) m |= GetMouseButtonModifier(_pressedButton);
        _browserHost.SendMouseMoveEvent(new CefMouseEvent { X = (int)p.X, Y = (int)p.Y, Modifiers = m }, false);
    }

    private CefEventFlags GetMouseButtonModifier(CefMouseButtonType b) => b switch
    { CefMouseButtonType.Left => CefEventFlags.LeftMouseButton, CefMouseButtonType.Right => CefEventFlags.RightMouseButton, CefMouseButtonType.Middle => CefEventFlags.MiddleMouseButton, _ => CefEventFlags.None };

    private void SendMouseButtonEvent(InputEventMouseButton e)
    {
        if (_browserHost == null) return;
        var p = GetLocalMousePosition(); var me = new CefMouseEvent { X = (int)p.X, Y = (int)p.Y, Modifiers = GetModifiers(e) };
        var b = ConvertMouseButton(e.ButtonIndex);
        if (e.ButtonIndex == MouseButton.WheelUp || e.ButtonIndex == MouseButton.WheelDown) { _browserHost.SendMouseWheelEvent(me, 0, e.ButtonIndex == MouseButton.WheelUp ? 120 : -120); return; }
        if (b == (CefMouseButtonType)(-1)) return;
        if (e.Pressed)
        {
            var t = Godot.Time.Singleton.GetTicksMsec() / 1000.0; _clickCount = t - _lastClickTime < DoubleClickInterval ? _clickCount + 1 : 1; _lastClickTime = t;
            _pressedButton = b; _isMousePressed = true; _browserHost.SendMouseClickEvent(me, b, false, _clickCount);
            // 左键抓取 Godot 焦点并通知 CEF 获焦。
            // IME 激活/关闭由 JS focusin/focusout 事件驱动（OnInputFocusChanged），
            // 页面告知 C# 当前是否有可编辑元素聚焦时才激活 IME。
            if (b == CefMouseButtonType.Left)
            {
                GrabFocus(); _browserHost?.SetFocus(true);
            }
        }
        else { _isMousePressed = false; _pressedButton = (CefMouseButtonType)(-1); _browserHost.SendMouseClickEvent(me, b, true, 1); }
    }

    private void SendKeyEvent(InputEventKey e)
    {
        var wk = GetWindowsKeyCode(e.Keycode);
        var ke = new CefKeyEvent { EventType = e.Pressed ? CefKeyEventType.KeyDown : CefKeyEventType.KeyUp, Modifiers = GetModifiers(e), WindowsKeyCode = wk, NativeKeyCode = (int)e.PhysicalKeycode, IsSystemKey = false };
        _browserHost.SendKeyEvent(ke);
        if (e.Pressed && e.Unicode.Value != 0 && !IsSpecialKey(e.Keycode))
            _browserHost.SendKeyEvent(new CefKeyEvent { EventType = CefKeyEventType.Char, WindowsKeyCode = (int)e.Unicode.Value, NativeKeyCode = (int)e.Unicode.Value, Modifiers = GetModifiers(e), Character = (char)e.Unicode.Value });
    }

    private int GetWindowsKeyCode(Key k) => k switch
    { Key.Backspace => 0x08, Key.Tab => 0x09, Key.Enter => 0x0D, Key.Shift => 0x10, Key.Ctrl => 0x11, Key.Alt => 0x12, Key.Pause => 0x13, Key.Capslock => 0x14, Key.Escape => 0x1B, Key.Space => 0x20, Key.Pageup => 0x21, Key.Pagedown => 0x22, Key.End => 0x23, Key.Home => 0x24, Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28, Key.Insert => 0x2D, Key.Delete => 0x2E, Key.F1 => 0x70, Key.F2 => 0x71, Key.F3 => 0x72, Key.F4 => 0x73, Key.F5 => 0x74, Key.F6 => 0x75, Key.F7 => 0x76, Key.F8 => 0x77, Key.F9 => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B, Key.Numlock => 0x90, Key.Scrolllock => 0x91, _ => (int)k };

    private bool IsSpecialKey(Key k) => k switch
    { Key.Backspace or Key.Tab or Key.Enter or Key.Escape or Key.Delete or Key.Insert or Key.Home or Key.End or Key.Pageup or Key.Pagedown or Key.Left or Key.Right or Key.Up or Key.Down or Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12 => true, _ => false };

    private CefEventFlags GetModifiers(InputEventWithModifiers e)
    { var m = CefEventFlags.None; if (e.ShiftPressed) m |= CefEventFlags.ShiftDown; if (e.CtrlPressed) m |= CefEventFlags.ControlDown; if (e.AltPressed) m |= CefEventFlags.AltDown; if (e.MetaPressed) m |= CefEventFlags.AltGrDown; return m; }

    private CefMouseButtonType ConvertMouseButton(MouseButton b) => b switch
    { MouseButton.Left => CefMouseButtonType.Left, MouseButton.Right => CefMouseButtonType.Right, MouseButton.Middle => CefMouseButtonType.Middle, _ => (CefMouseButtonType)(-1) };

    // ── 焦点管理 ────────────────────────────────────────

    protected override void _Notification(int what)
    {
        if (Godot.Engine.Singleton.IsEditorHint()) return;
        switch ((long)what)
        {
            case NotificationResized: break;
            case NotificationVisibilityChanged:
                if (_renderMode == RenderMode.EmbeddedWindow && _cefChildHwnd != IntPtr.Zero)
                    NativeWindowMethods.SetPlatformWindowVisible(_cefChildHwnd, Visible);
                break;
            case NotificationMouseExit: _isMousePressed = false; _pressedButton = (CefMouseButtonType)(-1); break;
            case NotificationFocusEnter:
                _browserHost?.SetFocus(true);
                // CefGlueControl 重新获得 Godot 焦点时，如果 JS 之前报告有输入框聚焦，
                // 重新激活 IME（解决首次运行/刷新后 IME 无法切换的问题）
                if (_imeWanted) ActivateIme();
                break;
            case NotificationFocusExit:
                if (_renderMode == RenderMode.EmbeddedWindow)
                {
                    ReleaseCefFocus();
                }
                else if (_renderMode == RenderMode.OSR)
                {
                    _browserHost?.SetFocus(false);
                    DeactivateIme();
                }
                break;
        }
    }

    /// <summary>
    /// 全局输入检测：点击 Godot 控件时释放 CEF 子 HWND 的键盘焦点。
    /// 嵌入模式下 CEF 子 HWND 会截获鼠标事件导致 Godot 的 NotificationFocusExit 不触发，
    /// 用 _Input 兜底检测点击行为。
    /// OSR 模式下同样需要此兜底，确保点击控件外时 CEF 释放内部焦点。
    /// </summary>
    protected override void _Input(InputEvent @event)
    {
        if (Godot.Engine.Singleton.IsEditorHint()) return;
        if (@event is InputEventMouseButton btn && btn.Pressed)
        {
            var mousePos = GetLocalMousePosition();
            if (mousePos.X < 0 || mousePos.Y < 0 || mousePos.X > Size.X || mousePos.Y > Size.Y)
            {
                if (_renderMode == RenderMode.EmbeddedWindow)
                {
                    ReleaseCefFocus();
                }
                else if (_renderMode == RenderMode.OSR)
                {
                    _browserHost?.SetFocus(false);
                    DeactivateIme();
                }
            }
        }
    }

    private void ReleaseCefFocus()
    {
        _browserHost?.SetFocus(false);
        if (_godotHwnd != IntPtr.Zero)
            NativeWindowMethods.SetPlatformFocus(_godotHwnd);
    }

    /// <summary>
    /// 由 GodotFocusHandler.OnTakeFocus 调用，当 CEF 即将失去焦点时同步 Godot 侧状态。
    /// </summary>
    internal void OnCefTakeFocus()
    {
        if (_renderMode == RenderMode.OSR)
        {
            DeactivateIme();
        }
    }

    /// <summary>
    /// 更新 IME 候选窗位置，由 GodotRenderHandler.OnImeCompositionRangeChanged 调用。
    /// </summary>
    internal void UpdateImePosition(int x, int y, int width, int height)
    {
        var globalPos = _cachedGlobalPosition;
        float scale = _cachedContentScale;
        int screenX = (int)((globalPos.X + x / scale) * scale);
        int screenY = (int)((globalPos.Y + y / scale) * scale);
    }
}