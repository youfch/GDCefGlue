extends Control

var _browser
var _log: RichTextLabel
var _custom_code: LineEdit

func _ready() -> void:
	_browser = $Browser
	_log = $LogPanel/Log
	_custom_code = $Toolbar/CustomCode

	_browser.initial_url = "about:blank"
	_browser.frame_rate = 60
	_browser.browser_initialized.connect(_on_browser_ready)
	_browser.load_end.connect(_on_load_end)
	_browser.eval_completed.connect(_on_eval_completed)
	_browser.bridge_request.connect(_on_bridge_request)

	$Toolbar/ButtonRow/BtnEvalTitle.pressed.connect(_on_eval.bind("document.title"))
	$Toolbar/ButtonRow/BtnEvalUrl.pressed.connect(_on_eval.bind("location.href"))
	$Toolbar/ButtonRow/BtnEvalMath.pressed.connect(_on_eval.bind("Math.PI * 2"))
	$Toolbar/ButtonRow/BtnClearLog.pressed.connect(_clear_log)
	$Toolbar/BtnEvalCustom.pressed.connect(_on_custom_eval)

func _on_browser_ready() -> void:
	_log_add("Browser ready")
	_register_bridge()
	# Load test page on first load
	_load_test_html()

func _on_load_end() -> void:
	# Re-register JS handler after each page load (V8 binding lost on navigation)
	# Don't reload test.html to avoid infinite loop
	_register_bridge()

func _register_bridge() -> void:
	_browser.register_js_handler("dotnetBridge", Callable(self, "_on_js_call"))
	_log_add("dotnetBridge registered, JS can call window.dotnetBridge.*")
	_inject_bridge()

func _inject_bridge() -> void:
	var js = """
(function() {
	if (window.__hostBridge) return;
	var pending = {};
	window.__hostBridge = {
		_onMessage: function(m){},
		_onResponse: function(id,msg){
			if(pending[id]){ pending[id](msg); delete pending[id]; }
		}
	};
})();"""
	_browser.execute_javascript(js)

func _load_test_html() -> void:
	var file = FileAccess.open("res://demo/ipc/test.html", FileAccess.READ)
	if file == null:
		_log_add("❌ test.html not found")
		return
	_browser.address = "data:text/html;charset=utf-8," + _uri_encode(file.get_as_text())
	_log_add("Test page loaded")

static func _uri_encode(s: String) -> String:
	var hex = "0123456789ABCDEF"
	var result = ""
	for b in s.to_utf8_buffer():
		var c = b as int
		if (c >= 0x30 and c <= 0x39) or (c >= 0x41 and c <= 0x5A) or (c >= 0x61 and c <= 0x7A) or c == 0x2D or c == 0x5F or c == 0x2E or c == 0x7E:
			result += char(c)
		else:
			result += "%" + hex[c >> 4] + hex[c & 0xF]
	return result

# ── JS Bridge handler ──

func _on_js_call(method_name: String, args_json: String) -> Variant:
	# From CEF thread, defer UI ops
	_log_add("← JS call: " + method_name)
	match method_name:
		"hello":
			return "Hello from GDScript!"
		"echo":
			var arr = JSON.parse_string(args_json) as Array
			return "GDScript echoes: " + str(arr[0] if arr and arr.size() > 0 else "")
		"add":
			var arr = JSON.parse_string(args_json) as Array
			return int(arr[0]) + int(arr[1]) if arr and arr.size() >= 2 else 0
		"getVersion":
			return "GDCefGlue GDE 1.0 + CefGlue 149"
		"eval":
			var arr = JSON.parse_string(args_json) as Array
			if arr and arr.size() > 0: _browser.eval_js(str(arr[0]))
			return "eval started"
	return "unknown method: " + method_name

# ── Eval buttons ──

func _on_eval(code: String) -> void:
	_log_add("→ eval " + code)
	_browser.eval_js(code)

func _on_custom_eval() -> void:
	var code = _custom_code.text.strip_edges()
	if code.is_empty(): code = "1 + 2 + 3"; _custom_code.text = code
	_log_add("→ custom eval " + code)
	_browser.eval_js(code)

func _on_eval_completed(result: String, error: String) -> void:
	if error and not error.is_empty(): _log_add("✗ " + error)
	else: _log_add("← " + result)

func _on_bridge_request(type: String, payload: String, cb_id: String) -> void:
	_log_add("← bridge request: " + type)

func _clear_log() -> void: _log.clear()

func _log_add(msg: String) -> void:
	# May come from CEF thread, use call_deferred for thread safety
	var time = Time.get_time_string_from_system()
	call_deferred("_log_add_deferred", time, msg)

func _log_add_deferred(time: String, msg: String) -> void:
	if _log == null: return
	_log.add_text("[" + time + "] " + msg + "\n")
	_log.scroll_to_line(9999)
