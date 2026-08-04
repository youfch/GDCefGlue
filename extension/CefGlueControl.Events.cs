using System;
using System.Text.Json;
using Godot;

namespace GDCefGlueExtension;

// ── ForwardInputEvents 事件转发 + 焦点监视器 ────────────────
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
        using var frame = _browser.GetMainFrame();
        if (frame != null)
            frame.ExecuteJavaScript(EventForwardingScript, "godot://event_forward", 0);
    }

    /// <summary>
    /// 注册 __hostFocus V8 对象 + 注入输入焦点监听 JS。
    /// 页面通过 focusin/focusout 事件告知 C# 当前是否有可编辑元素聚焦，
    /// 驱动 IME 激活/关闭。OSR 和 EmbeddedWindow 模式均适用。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(GodotFocusWatcher))]
    internal void InjectFocusWatcherIfNeeded()
    {
        if (_browser == null || !_browserCreated) return;
        if (!_focusWatcherRegistered)
        {
            RegisterJavascriptObject(new GodotFocusWatcher(this), "__hostFocus");
            _focusWatcherRegistered = true;
        }
        using var frame = _browser.GetMainFrame();
        if (frame != null)
            frame.ExecuteJavaScript(FocusWatcherScript, "godot://focus_watcher", 0);
    }

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

    private void HandleForwardedKeyEvent(JsonElement root, bool pressed)
    {
        int keyCode = root.GetProperty("keyCode").GetInt32();
        string keyStr = root.GetProperty("key").GetString();
        bool repeat = root.TryGetProperty("repeat", out var r) && r.GetBoolean();

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
            >= 0x41 and <= 0x5A => (Key)(keyCode + 32),
            >= 0x30 and <= 0x39 => (Key)keyCode,
            _ => (Key)keyCode
        };

        var evt = new InputEventKey
        {
            Pressed = pressed,
            Keycode = godotKey,
            PhysicalKeycode = godotKey,
            Unicode = (!string.IsNullOrEmpty(keyStr) && keyStr.Length == 1 && keyStr[0] >= 32)
                ? System.Text.Rune.GetRuneAt(keyStr, 0) : default,
            Echo = repeat,
            ShiftPressed = root.TryGetProperty("shift", out var s) && s.GetBoolean(),
            CtrlPressed = root.TryGetProperty("ctrl", out var c) && c.GetBoolean(),
            AltPressed = root.TryGetProperty("alt", out var a) && a.GetBoolean(),
            MetaPressed = root.TryGetProperty("meta", out var m) && m.GetBoolean()
        };

        CallDeferred(nameof(PushInputEvent), evt);
    }

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
            viewport.PushInput(evt);
    }

    // ══════════════════════════════════════════════════════════════
    //  IME 光标位置追踪（JS → V8 → C#）
    // ══════════════════════════════════════════════════════════════

    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(GodotCaretTracker))]
    internal void InjectCaretTrackerIfNeeded()
    {
        if (_browser == null || !_browserCreated) return;
        if (_renderMode != RenderMode.OSR) return;
        if (!_caretTrackerRegistered)
        {
            RegisterJavascriptObject(new GodotCaretTracker(this), "__hostCaret");
            _caretTrackerRegistered = true;
        }
        using var frame = _browser.GetMainFrame();
        if (frame != null)
            frame.ExecuteJavaScript(CaretTrackerScript, "godot://caret_tracker", 0);
    }

    private bool _caretTrackerRegistered;

    private sealed class GodotCaretTracker
    {
        private readonly CefGlueControl _control;
        public GodotCaretTracker(CefGlueControl control) => _control = control;
        public void OnCaretPositionChanged(int x, int y, int height, double dpr)
        {
            _control.CallDeferred("_handle_caret_position", x, y, height, dpr);
        }
    }

    private void _handle_caret_position(int x, int y, int height, double dpr)
    {
        if (_disposed || _browserHost == null) return;

        float scale = _cachedContentScale;
        var globalPos = _cachedGlobalPosition;

        // 相对窗口坐标（WindowSetImePosition 使用窗口客户区坐标）
        // jsPos * dpr: CSS 像素 → 物理像素（处理页面缩放）
        // globalPos * scale: 控件位置从虚拟像素 → 物理像素
        // Y +10 防止 IME 候选窗遮挡输入
        int screenX = (int)(x * dpr + globalPos.X * scale);
        int screenY = (int)(y * dpr + globalPos.Y * scale + 10);

        DisplayServer.Singleton.WindowSetImePosition(new Vector2I(screenX, screenY));
    }

    private const string CaretTrackerScript = @"
(function(){
    if (window.__hostCaretInjected) return;
    window.__hostCaretInjected = true;

    var IME_ACTIVE = false;

    function isEditable(el) {
        return el && (el.isContentEditable || el.tagName === 'INPUT' || el.tagName === 'TEXTAREA');
    }

    var MIRROR_PROPS = [
        'direction','boxSizing','width','height','overflowX','overflowY',
        'borderTopWidth','borderRightWidth','borderBottomWidth','borderLeftWidth',
        'borderStyle','paddingTop','paddingRight','paddingBottom','paddingLeft',
        'fontStyle','fontVariant','fontWeight','fontStretch','fontSize',
        'fontSizeAdjust','lineHeight','fontFamily','textAlign','textTransform',
        'textIndent','textDecoration','letterSpacing','wordSpacing',
        'tabSize','MozTabSize','whiteSpace','wordWrap','wordBreak'
    ];

    function getCaretPos(element, pos) {
        var isInput = element.tagName === 'INPUT';
        var style = window.getComputedStyle(element);
        var mirror = document.createElement('div');
        mirror.id = '__ime_mirror';
        document.body.appendChild(mirror);
        var ms = mirror.style;
        ms.position = 'absolute'; ms.visibility = 'hidden';
        ms.whiteSpace = isInput ? 'nowrap' : 'pre-wrap';
        ms.wordWrap = isInput ? 'normal' : 'break-word';
        for (var i = 0; i < MIRROR_PROPS.length; i++) {
            var p = MIRROR_PROPS[i];
            if (p === 'whiteSpace' || p === 'wordWrap') continue;
            ms[p] = style[p];
        }
        if (isInput) { ms.height = 'auto'; ms.overflowY = 'visible'; }
        var elRect = element.getBoundingClientRect();
        ms.left = elRect.left + window.scrollX + 'px';
        ms.top = elRect.top + window.scrollY + 'px';
        var textBefore = element.value.substring(0, pos);
        mirror.textContent = textBefore;
        if (textBefore.endsWith('\n')) mirror.textContent += '\u200b';
        var marker = document.createElement('span');
        marker.textContent = '\u200b';
        mirror.appendChild(marker);
        var markerRect = marker.getBoundingClientRect();
        document.body.removeChild(mirror);
        return {
            x: markerRect.left - element.scrollLeft,
            y: markerRect.top - element.scrollTop,
            height: parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.2
        };
    }

    function reportCaret() {
        try {
            var el = document.activeElement;
            if (!el || !isEditable(el)) return;
            var x, y, h;
            if (el.isContentEditable) {
                var sel = window.getSelection();
                if (sel && sel.rangeCount > 0) {
                    var range = sel.getRangeAt(0);
                    var rects = range.getClientRects();
                    var r = rects.length > 0 ? rects[rects.length - 1] : range.getBoundingClientRect();
                    x = Math.round(r.left || r.x || 0);
                    y = Math.round(r.top || r.y || 0);
                    h = Math.round(r.height || 20);
                } else { return; }
            } else {
                var pos = el.selectionStart || 0;
                var c = getCaretPos(el, pos);
                x = Math.round(c.x); y = Math.round(c.y); h = Math.round(c.height);
            }
            window.__hostCaret.onCaretPositionChanged(x, y, h, window.devicePixelRatio);
        } catch(e) { if (console && console.error) console.error('Caret tracker:', e); }
    }

    function scheduleReport() { setTimeout(reportCaret, 0); }

    document.addEventListener('selectionchange', function() {
        if (isEditable(document.activeElement)) reportCaret();
    });
    document.addEventListener('input', function() { scheduleReport(); }, true);
    document.addEventListener('keyup', function(e) {
        var nav = ['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown','Backspace','Delete'];
        if (nav.indexOf(e.key) >= 0) reportCaret();
    }, true);
    document.addEventListener('mouseup', function() { setTimeout(reportCaret, 10); }, true);
    document.addEventListener('focusin', function() { scheduleReport(); }, true);
})();
";
}