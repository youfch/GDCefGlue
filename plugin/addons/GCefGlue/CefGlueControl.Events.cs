using System;
using System.Text.Json;
using Godot;

namespace GDCefGlue
{
    // ── 事件穿透（JS → C# → Godot）— partial class 拆分 ─────────────
    // 参考 godot_wry 的 JS 事件监听 + IPC 转发模式
    public partial class CefGlueControl
    {
        /// <summary>
        /// 事件转发 JS。注入到每个页面，通过 godot://bridge 机制将浏览器内的
        /// 鼠标/键盘事件转发到 Godot，实现事件穿透。
        ///
        /// 使用单个隐藏 iframe 复用，避免每帧创建/销毁 DOM 元素。
        /// 参考 godot_wry 的 JS 事件监听 + IPC 转发模式。
        /// </summary>
        private const string EventForwardingScript = @"
(function(){
    if (window.__godotEventForwardInjected) return;
    window.__godotEventForwardInjected = true;

    // 创建单个隐藏 iframe 用于 godot://bridge 通信
    var _f = document.createElement('iframe');
    _f.style.cssText = 'display:none!important;width:0!important;height:0!important;border:none!important';
    document.documentElement.appendChild(_f);

    function godotEvent(type, data) {
        var p = {eventType: type};
        for (var k in data) { p[k] = data[k]; }
        _f.src = 'godot://bridge?type=event_forward&payload=' + encodeURIComponent(JSON.stringify(p));
    }

    // ── 鼠标事件 ──
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

    // ── 键盘事件 ──
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
        /// 在页面加载完成后注入事件转发 JS（仅嵌入模式且 ForwardInputEvents 启用时）。
        /// 在 OnLoadEnd 中调用，确保每个页面都有事件转发能力。
        /// </summary>
        internal void InjectEventForwardingScriptIfNeeded()
        {
            if (_renderMode != RenderMode.EmbeddedWindow || _browser == null || !ForwardInputEvents)
                return;

            var frame = _browser.GetMainFrame();
            if (frame != null)
            {
                frame.ExecuteJavaScript(EventForwardingScript, "godot://event_forward", 0);
            }
        }

        /// <summary>
        /// 处理从 JS 转发过来的事件，构造 Godot InputEvent 并推回 Godot 事件系统。
        /// 坐标映射：JS clientX/clientY（CEF 物理像素）→ Godot 虚拟像素坐标
        ///   godotX = control.GlobalPosition.X + (cefX / contentScale)
        ///   godotY = control.GlobalPosition.Y + (cefY / contentScale)
        /// </summary>
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
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[CefGlueControl] Failed to handle forwarded event: {ex.Message}");
            }
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