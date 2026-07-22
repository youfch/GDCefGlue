using System;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    // ══════════════════════════════════════════════════════════════
    //  Godot → CEF 输入转发（仅 OSR 模式）
    // ══════════════════════════════════════════════════════════════
    public partial class CefGlueControl
    {
        // ── IME 状态跟踪 ──
        private bool _imeActive;

        private void ActivateIme()
        {
            var window = GetWindow();
            if (window != null && HasFocus() && !_imeActive)
            {
                window.SetImeActive(true);
                _imeActive = true;
            }
        }

        private void DeactivateIme()
        {
            var window = GetWindow();
            if (window != null && _imeActive)
            {
                window.SetImeActive(false);
                _imeActive = false;
            }
        }

        /// <summary>
        /// 由 GodotRenderHandler.OnImeCompositionRangeChanged 调用，更新 IME 候选窗位置。
        /// IME 激活/关闭由 JS focusin/focusout 事件驱动，见 OnInputFocusChanged。
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
        /// 注意：此回调从 CEF IPC 线程进入，需要用 CallDeferred 调度到 Godot 主线程执行 IME 操作。
        /// </summary>
        internal void OnInputFocusChanged(bool focused)
        {
            if (focused) CallDeferred(nameof(ActivateIme));
            else CallDeferred(nameof(DeactivateIme));
        }

        /// <summary>
        /// JS → C# 桥接对象：页面通过 window.__hostFocus.onInputFocusChanged(bool) 通知 C# 输入焦点变化。
        /// </summary>
        private sealed class GodotFocusWatcher
        {
            private readonly CefGlueControl _control;
            public GodotFocusWatcher(CefGlueControl control) => _control = control;
            public void OnInputFocusChanged(bool focused) => _control.OnInputFocusChanged(focused);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (_browserHost == null || _renderMode == RenderMode.EmbeddedWindow) return;
            switch (@event)
            {
                case InputEventMouseMotion m: SendMouseMoveEvent(m); break;
                case InputEventMouseButton b: SendMouseButtonEvent(b); break;
                case InputEventKey k:
                    SendKeyEvent(k);
                    // 拦截导航键（Tab/Home/End/方向键）防止焦点从浏览器控件逃逸，
                    // 与 CefGlue WPF/Avalonia 的 OnKeyDown 中 handled=true 等效。
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
                // 左键抓取 Godot 焦点并通知 CEF 获焦。
                // IME 激活/关闭由 JS focusin/focusout 事件驱动（OnInputFocusChanged），
                // 页面告知 C# 当前是否有可编辑元素聚焦时才激活 IME。
                if (button == CefMouseButtonType.Left)
                {
                    GrabFocus(); _browserHost?.SetFocus(true);
                }
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
            switch ((long)what)
            {
                case NotificationResized: break;
                case NotificationMouseExit: _isMousePressed = false; _pressedButton = (CefMouseButtonType)(-1); break;
                case NotificationFocusEnter:
                    _browserHost?.SetFocus(true);
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
        public override void _Input(InputEvent @event)
        {
            if (Engine.IsEditorHint()) return;
            if (@event is InputEventMouseButton btn && btn.Pressed)
            {
                // 如果点击不在 CEF 控件区域内，释放 CEF 焦点
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
        /// CEF 的 characterBounds 是相对于 view 坐标，转换为全局屏幕坐标。
        /// Godot 4.6 的 DisplayServer 不提供直接设置 IME 候选窗位置的 API，
        /// 候选窗位置由系统根据当前焦点控件位置自动确定。
        /// 此方法保留以备将来 Godot 版本支持 IME 位置控制。
        /// </summary>
        internal void UpdateImePosition(int x, int y, int width, int height)
        {
            // 将 CEF 内部坐标转换为全局屏幕坐标
            var globalPos = _cachedGlobalPosition;
            float scale = _cachedContentScale;
            int screenX = (int)((globalPos.X + x / scale) * scale);
            int screenY = (int)((globalPos.Y + y / scale) * scale);

            // 预留：Godot 未来版本可能支持 DisplayServer.ImeSetSelection 或类似 API
            // 目前保持 IME 激活状态正确即可，候选窗位置由系统自动定位
        }
    }
}