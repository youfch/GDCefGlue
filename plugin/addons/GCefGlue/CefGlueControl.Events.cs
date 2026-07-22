using System;
using System.Text.Json;
using Godot;

namespace GDCefGlue
{
    public partial class CefGlueControl
    {
        /// <summary>
        /// 事件转发 JS。注入到每个页面，通过 CEF IPC（RegisterJavascriptObject）将
        /// 浏览器内的鼠标/键盘事件转发到 Godot，实现事件穿透。
        /// </summary>
        private const string EventForwardingScript = @"
(function(){
    if (window.__hostEventForwardInjected) return;
    window.__hostEventForwardInjected = true;

    function godotEvent(type, data) {
        var p = {eventType: type};
        for (var k in data) { p[k] = data[k]; }
        window.__hostEvents.forward(JSON.stringify(p));
    }

    document.addEventListener('mousedown', function(e) {
        godotEvent('mouse_down', {
            button: e.button, x: e.clientX, y: e.clientY,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey, meta: e.metaKey
        });
    }, true);

    document.addEventListener('mouseup', function(e) {
        godotEvent('mouse_up', {
            button: e.button, x: e.clientX, y: e.clientY,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey, meta: e.metaKey
        });
    }, true);

    document.addEventListener('mousemove', function(e) {
        godotEvent('mouse_move', {
            x: e.clientX, y: e.clientY,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey, meta: e.metaKey
        });
    }, true);

    document.addEventListener('wheel', function(e) {
        godotEvent('mouse_wheel', {
            deltaX: e.deltaX, deltaY: e.deltaY, deltaZ: e.deltaZ,
            x: e.clientX, y: e.clientY
        });
    }, true);

    document.addEventListener('keydown', function(e) {
        godotEvent('key_down', {
            key: e.key, code: e.code, keyCode: e.keyCode,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey, meta: e.metaKey,
            repeat: e.repeat
        });
    }, true);

    document.addEventListener('keyup', function(e) {
        godotEvent('key_up', {
            key: e.key, code: e.code, keyCode: e.keyCode,
            ctrl: e.ctrlKey, shift: e.shiftKey, alt: e.altKey, meta: e.metaKey
        });
    }, true);
})();
";

        /// <summary>
        /// 注册 V8 对象 + 注入事件监听 JS。在 OnLoadEnd 中调用。
        /// </summary>
        internal void InjectEventForwardingScriptIfNeeded()
        {
            if (_renderMode != RenderMode.EmbeddedWindow || _browser == null || !ForwardInputEvents)
                return;
            RegisterEventForwarder();
            var frame = _browser.GetMainFrame();
            if (frame != null)
                frame.ExecuteJavaScript(EventForwardingScript, "godot://event_forward", 0);
        }

        /// <summary>
        /// 注册 __hostFocus V8 对象 + 注入输入焦点监听 JS。
        /// 页面通过 focusin/focusout 事件告知 C# 当前是否有可编辑元素聚焦，
        /// 驱动 IME 激活/关闭。OSR 和 EmbeddedWindow 模式均适用。
        /// </summary>
        internal void InjectFocusWatcherIfNeeded()
        {
            if (_browser == null || !_browserCreated) return;
            if (_focusWatcherRegistered) return;
            RegisterJavascriptObject(new GodotFocusWatcher(this), "__hostFocus");
            _focusWatcherRegistered = true;
            var frame = _browser.GetMainFrame();
            if (frame != null)
                frame.ExecuteJavaScript(FocusWatcherScript, "godot://focus_watcher", 0);
        }

        private bool _focusWatcherRegistered;

        private const string FocusWatcherScript = @"
(function(){
    if (window.__hostFocusInjected) return;
    window.__hostFocusInjected = true;

    function checkFocus() {
        var el = document.activeElement;
        if (!el) { window.__hostFocus.onInputFocusChanged(false); return; }
        var tag = el.tagName;
        var isInput = tag === 'INPUT' || tag === 'TEXTAREA' || el.isContentEditable;
        window.__hostFocus.onInputFocusChanged(isInput);
    }

    document.addEventListener('focusin', checkFocus, true);
    document.addEventListener('focusout', function() {
        setTimeout(checkFocus, 0);
    }, true);

    if (document.readyState === 'complete') checkFocus();
    else window.addEventListener('load', checkFocus);
})();
";

        /// <summary>
        /// 注册 __hostEvents V8 对象。仅嵌入模式。
        /// </summary>
        internal void RegisterEventForwarder()
        {
            if (_browser == null || !_browserCreated) return;
            if (_renderMode != RenderMode.EmbeddedWindow) return;
            if (!_eventForwarderRegistered)
            {
                RegisterJavascriptObject(new GodotEventForwarder(this), "__hostEvents");
                _eventForwarderRegistered = true;
            }
        }

        private sealed class GodotEventForwarder
        {
            private readonly CefGlueControl _control;
            public GodotEventForwarder(CefGlueControl control) => _control = control;
            public void Forward(string payload)
            {
                _control.HandleForwardedEvent(payload);
            }
        }

        private bool _eventForwarderRegistered;

        internal void HandleForwardedEvent(string payload)
        {
            if (string.IsNullOrEmpty(payload) || _browserHost == null)
                return;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                string eventType = root.GetProperty("eventType").GetString();

                switch (eventType)
                {
                    case "mouse_down":
                    case "mouse_up":
                        HandleForwardedMouseButton(root, eventType == "mouse_down");
                        break;
                    case "mouse_move":
                        HandleForwardedMouseMove(root);
                        break;
                    case "mouse_wheel":
                        HandleForwardedMouseWheel(root);
                        break;
                    case "key_down":
                    case "key_up":
                        HandleForwardedKeyEvent(root, eventType == "key_down");
                        break;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CefGlueControl] Failed to handle forwarded event: {ex.Message}");
            }
        }

        // ... existing mouse handlers ...

        private void HandleForwardedKeyEvent(JsonElement root, bool pressed)
        {
            int keyCode = root.GetProperty("keyCode").GetInt32();
            string keyStr = root.GetProperty("key").GetString();
            bool repeat = root.TryGetProperty("repeat", out var r) && r.GetBoolean();

            // Windows VK 到 Godot Key 的映射（大部分直接对应）
            // 特殊键需要额外处理
            var godotKey = keyCode switch
            {
                0x08 => Key.Backspace,
                0x09 => Key.Tab,
                0x0D => Key.Enter,
                0x10 => Key.Shift,
                0x11 => Key.Ctrl,
                0x12 => Key.Alt,
                0x1B => Key.Escape,
                0x20 => Key.Space,
                0x21 => Key.Pageup,
                0x22 => Key.Pagedown,
                0x23 => Key.End,
                0x24 => Key.Home,
                0x25 => Key.Left,
                0x26 => Key.Up,
                0x27 => Key.Right,
                0x28 => Key.Down,
                0x2D => Key.Insert,
                0x2E => Key.Delete,
                0x70 => Key.F1,   0x71 => Key.F2,  0x72 => Key.F3,  0x73 => Key.F4,
                0x74 => Key.F5,   0x75 => Key.F6,  0x76 => Key.F7,  0x77 => Key.F8,
                0x78 => Key.F9,   0x79 => Key.F10, 0x7A => Key.F11, 0x7B => Key.F12,
                0x90 => Key.Numlock,
                0x91 => Key.Scrolllock,
                // 字母键 A-Z (VK 0x41-0x5A)
                >= 0x41 and <= 0x5A => (Key)(keyCode + 32), // VK_A → Key.A (97)
                // 数字键 0-9 (VK 0x30-0x39)
                >= 0x30 and <= 0x39 => (Key)keyCode,
                _ => (Key)keyCode
            };

            // Unicode 字符（可打印键取第一个字符）
            uint unicode = 0;
            if (!string.IsNullOrEmpty(keyStr) && keyStr.Length == 1 && keyStr[0] >= 32)
                unicode = keyStr[0];

            var evt = new InputEventKey
            {
                Pressed = pressed,
                Keycode = godotKey,
                PhysicalKeycode = godotKey,
                Unicode = unicode,
                Echo = repeat,
                ShiftPressed = root.TryGetProperty("shift", out var s) && s.GetBoolean(),
                CtrlPressed = root.TryGetProperty("ctrl", out var c) && c.GetBoolean(),
                AltPressed = root.TryGetProperty("alt", out var a) && a.GetBoolean(),
                MetaPressed = root.TryGetProperty("meta", out var m) && m.GetBoolean()
            };

            CallDeferred(nameof(PushInputEvent), evt);
        }

        /// <summary>
        /// 将 CEF 浏览器内的物理像素坐标转换为 Godot 虚拟像素坐标。
        /// </summary>
        private static Vector2 CefToGodotCoord(float cefX, float cefY, Vector2 controlGlobalPos, float contentScale)
        {
            return new Vector2(controlGlobalPos.X + (cefX / contentScale), controlGlobalPos.Y + (cefY / contentScale));
        }

        private void HandleForwardedMouseButton(JsonElement root, bool pressed)
        {
            int button = root.GetProperty("button").GetInt32();
            float cefX = root.GetProperty("x").GetSingle();
            float cefY = root.GetProperty("y").GetSingle();
            var godotPos = CefToGodotCoord(cefX, cefY, _cachedGlobalPosition, _cachedContentScale);

            var evt = new InputEventMouseButton
            {
                Pressed = pressed,
                Position = godotPos,
                GlobalPosition = godotPos,
                ButtonIndex = button switch
                {
                    0 => MouseButton.Left,
                    1 => MouseButton.Middle,
                    2 => MouseButton.Right,
                    _ => MouseButton.Left
                },
                ShiftPressed = root.TryGetProperty("shift", out var s) && s.GetBoolean(),
                CtrlPressed = root.TryGetProperty("ctrl", out var c) && c.GetBoolean(),
                AltPressed = root.TryGetProperty("alt", out var a) && a.GetBoolean()
            };

            CallDeferred(nameof(PushInputEvent), evt);
        }

        private void HandleForwardedMouseMove(JsonElement root)
        {
            float cefX = root.GetProperty("x").GetSingle();
            float cefY = root.GetProperty("y").GetSingle();
            var godotPos = CefToGodotCoord(cefX, cefY, _cachedGlobalPosition, _cachedContentScale);

            var evt = new InputEventMouseMotion
            {
                Position = godotPos,
                GlobalPosition = godotPos,
                Relative = Vector2.Zero,
                ShiftPressed = root.TryGetProperty("shift", out var s) && s.GetBoolean(),
                CtrlPressed = root.TryGetProperty("ctrl", out var c) && c.GetBoolean(),
                AltPressed = root.TryGetProperty("alt", out var a) && a.GetBoolean()
            };

            CallDeferred(nameof(PushInputEvent), evt);
        }

        private void HandleForwardedMouseWheel(JsonElement root)
        {
            float deltaY = root.GetProperty("deltaY").GetSingle();
            float cefX = root.GetProperty("x").GetSingle();
            float cefY = root.GetProperty("y").GetSingle();
            var godotPos = CefToGodotCoord(cefX, cefY, _cachedGlobalPosition, _cachedContentScale);

            var evt = new InputEventMouseButton
            {
                Position = godotPos,
                GlobalPosition = godotPos,
                ButtonIndex = deltaY > 0 ? MouseButton.WheelUp : MouseButton.WheelDown,
                Pressed = true,
                Factor = Math.Abs(deltaY) / 120.0f
            };

            // 先触按下，再触释放（模拟滚动的一次性脉冲）
            CallDeferred(nameof(PushInputEvent), evt);
            var evtUp = new InputEventMouseButton
            {
                Position = godotPos,
                GlobalPosition = godotPos,
                ButtonIndex = evt.ButtonIndex,
                Pressed = false,
                Factor = evt.Factor
            };
            CallDeferred(nameof(PushInputEvent), evtUp);
        }

        private void PushInputEvent(InputEvent evt)
        {
            var viewport = GetViewport();
            if (viewport != null)
            {
                viewport.PushInput(evt);
            }
        }
    }
}