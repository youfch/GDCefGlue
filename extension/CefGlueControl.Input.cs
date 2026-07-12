using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    private void ActivateIme() { var w = GetWindow(); if (w != null && HasFocus()) w.SetImeActive(true); }
    private void DeactivateIme() { var w = GetWindow(); if (w != null) w.SetImeActive(false); }

    protected override void _GuiInput(InputEvent @event)
    {
        if (_browserHost == null) return;
        switch (@event) { case InputEventMouseMotion m: SendMouseMoveEvent(m); break; case InputEventMouseButton b: SendMouseButtonEvent(b); break; case InputEventKey k: SendKeyEvent(k); break; }
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
            GrabFocus(); _browserHost?.SetFocus(true);
            var w = GetWindow(); if (w != null) w.SetImePosition(new Vector2I((int)p.X, (int)p.Y)); ActivateIme();
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
}