using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Godot → CEF 输入转发（仅 OSR 模式）
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        private void ActivateIme()
        {
            var window = GetWindow();
            if (window != null && HasFocus()) window.SetImeActive(true);
        }

        private void DeactivateIme()
        {
            var window = GetWindow();
            if (window != null) window.SetImeActive(false);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (_browserHost == null || _renderMode == RenderMode.EmbeddedWindow) return;
            switch (@event)
            {
                case InputEventMouseMotion m: SendMouseMoveEvent(m); break;
                case InputEventMouseButton b: SendMouseButtonEvent(b); break;
                case InputEventKey k: SendKeyEvent(k); break;
            }
        }

        private void SendMouseMoveEvent(InputEventMouseMotion e)
        {
            if (_browserHost == null) return;
            var localPos = GetLocalMousePosition();
            var modifiers = GetModifiers(e);
            if (_isMousePressed && _pressedButton != (CefMouseButtonType)(-1))
                modifiers |= GetMouseButtonModifier(_pressedButton);
            _browserHost.SendMouseMoveEvent(new CefMouseEvent { X = (int)localPos.X, Y = (int)localPos.Y, Modifiers = modifiers }, false);
        }

        private CefEventFlags GetMouseButtonModifier(CefMouseButtonType button) => button switch
        {
            CefMouseButtonType.Left => CefEventFlags.LeftMouseButton,
            CefMouseButtonType.Right => CefEventFlags.RightMouseButton,
            CefMouseButtonType.Middle => CefEventFlags.MiddleMouseButton,
            _ => CefEventFlags.None
        };

        private void SendMouseButtonEvent(InputEventMouseButton e)
        {
            if (_browserHost == null) return;
            var localPos = GetLocalMousePosition();
            var mouseEvent = new CefMouseEvent { X = (int)localPos.X, Y = (int)localPos.Y, Modifiers = GetModifiers(e) };
            var button = ConvertMouseButton(e.ButtonIndex);
            if (e.ButtonIndex == MouseButton.WheelUp || e.ButtonIndex == MouseButton.WheelDown)
            { _browserHost.SendMouseWheelEvent(mouseEvent, 0, e.ButtonIndex == MouseButton.WheelUp ? 120 : -120); return; }
            if (button == (CefMouseButtonType)(-1)) return;
            if (e.Pressed)
            {
                var currentTime = Time.GetTicksMsec() / 1000.0;
                if (currentTime - _lastClickTime < DoubleClickInterval) _clickCount++; else _clickCount = 1;
                _lastClickTime = currentTime;
                _pressedButton = button; _isMousePressed = true;
                _browserHost.SendMouseClickEvent(mouseEvent, button, false, _clickCount);
                GrabFocus(); _browserHost?.SetFocus(true); ActivateIme();
            }
            else
            {
                _isMousePressed = false; _pressedButton = (CefMouseButtonType)(-1);
                _browserHost.SendMouseClickEvent(mouseEvent, button, true, 1);
            }
        }

        private void SendKeyEvent(InputEventKey e)
        {
            var windowsKeyCode = GetWindowsKeyCode(e.Keycode);
            var keyEvent = new CefKeyEvent
            {
                EventType = e.Pressed ? CefKeyEventType.KeyDown : CefKeyEventType.KeyUp,
                Modifiers = GetModifiers(e), WindowsKeyCode = windowsKeyCode,
                NativeKeyCode = (int)e.PhysicalKeycode, IsSystemKey = false
            };
            _browserHost.SendKeyEvent(keyEvent);
            if (e.Pressed && e.Unicode != 0 && !IsSpecialKey(e.Keycode))
                _browserHost.SendKeyEvent(new CefKeyEvent
                {
                    EventType = CefKeyEventType.Char, WindowsKeyCode = (int)e.Unicode,
                    NativeKeyCode = (int)e.Unicode, Modifiers = GetModifiers(e), Character = (char)e.Unicode
                });
        }

        private int GetWindowsKeyCode(Key keycode) => keycode switch
        {
            Key.Backspace => 0x08, Key.Tab => 0x09, Key.Enter => 0x0D, Key.Shift => 0x10,
            Key.Ctrl => 0x11, Key.Alt => 0x12, Key.Pause => 0x13, Key.Capslock => 0x14,
            Key.Escape => 0x1B, Key.Space => 0x20, Key.Pageup => 0x21, Key.Pagedown => 0x22,
            Key.End => 0x23, Key.Home => 0x24, Key.Left => 0x25, Key.Up => 0x26,
            Key.Right => 0x27, Key.Down => 0x28, Key.Insert => 0x2D, Key.Delete => 0x2E,
            Key.F1 => 0x70, Key.F2 => 0x71, Key.F3 => 0x72, Key.F4 => 0x73,
            Key.F5 => 0x74, Key.F6 => 0x75, Key.F7 => 0x76, Key.F8 => 0x77,
            Key.F9 => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,
            Key.Numlock => 0x90, Key.Scrolllock => 0x91, _ => (int)keycode
        };

        private bool IsSpecialKey(Key keycode) => keycode switch
        {
            Key.Backspace or Key.Tab or Key.Enter or Key.Escape or Key.Delete or Key.Insert
                or Key.Home or Key.End or Key.Pageup or Key.Pagedown
                or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6
                or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12 => true,
            _ => false
        };

        private CefEventFlags GetModifiers(InputEventWithModifiers e)
        {
            var m = CefEventFlags.None;
            if (e.ShiftPressed) m |= CefEventFlags.ShiftDown;
            if (e.CtrlPressed) m |= CefEventFlags.ControlDown;
            if (e.AltPressed) m |= CefEventFlags.AltDown;
            if (e.MetaPressed) m |= CefEventFlags.AltGrDown;
            return m;
        }

        private CefMouseButtonType ConvertMouseButton(MouseButton button) => button switch
        {
            MouseButton.Left => CefMouseButtonType.Left,
            MouseButton.Right => CefMouseButtonType.Right,
            MouseButton.Middle => CefMouseButtonType.Middle,
            _ => (CefMouseButtonType)(-1)
        };

        public override void _Notification(int what)
        {
            if (Engine.IsEditorHint()) return;
            if (_renderMode == RenderMode.EmbeddedWindow)
            {
                switch ((long)what)
                {
                    case NotificationResized: break;
                    case NotificationMouseExit: _isMousePressed = false; _pressedButton = (CefMouseButtonType)(-1); break;
                    case NotificationFocusEnter: _browserHost?.SetFocus(true); ActivateIme(); break;
                    case NotificationFocusExit: DeactivateIme(); break;
                }
            }
        }
    }
}