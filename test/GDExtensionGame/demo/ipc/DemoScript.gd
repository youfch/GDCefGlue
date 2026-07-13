extends Control

var _browser
var _log: RichTextLabel
var _custom_code: LineEdit

func _ready() -> void:
	_browser = $Browser
	_log = $LogPanel/Log
	_custom_code = $Toolbar/CustomCode

	_browser.InitialUrl = "about:blank"
	_browser.FrameRate = 60
	_browser.BrowserInitialized.connect(_on_browser_ready)
	_browser.LoadEnd.connect(_on_load_end)
	_browser.eval_completed.connect(_on_eval_completed)
	_browser.bridge_request.connect(_on_bridge_request)

	$Toolbar/ButtonRow/BtnEvalTitle.pressed.connect(_on_eval.bind("document.title"))
	$Toolbar/ButtonRow/BtnEvalUrl.pressed.connect(_on_eval.bind("location.href"))
	$Toolbar/ButtonRow/BtnEvalMath.pressed.connect(_on_eval.bind("Math.PI * 2"))
	$Toolbar/ButtonRow/BtnClearLog.pressed.connect(_clear_log)
	$Toolbar/BtnEvalCustom.pressed.connect(_on_custom_eval)

func _on_browser_ready() -> void:
	_log_add("浏览器已就绪")
	_register_bridge()
	# 首次加载测试页面
	_load_test_html()

func _on_load_end() -> void:
	# 每次页面加载后重新注册 JS handler（V8 绑定在导航后丢失）
	# 不重新加载 test.html，避免死循环
	_register_bridge()

func _register_bridge() -> void:
	_browser.RegisterJsHandler("dotnetBridge", Callable(self, "_on_js_call"))
	_log_add("已注册 dotnetBridge，JS 可通过 window.dotnetBridge.* 调用")
	_inject_bridge()

func _inject_bridge() -> void:
	var js = """
(function() {
	if (window._godotBridge) return;
	var pending = {};
	window._godotBridge = {
		_onMessage: function(m){},
		_onResponse: function(id,msg){
			if(pending[id]){ pending[id](msg); delete pending[id]; }
		}
	};
})();"""
	_browser.ExecuteJavaScript(js)

func _load_test_html() -> void:
	var file = FileAccess.open("res://demo/ipc/test.html", FileAccess.READ)
	if file == null:
		_log_add("❌ 找不到 test.html")
		return
	_browser.Address = "data:text/html;charset=utf-8," + _uri_encode(file.get_as_text())
	_log_add("已加载测试页面")

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

# ── JS Bridge 处理器 ──

func _on_js_call(method_name: String, args_json: String) -> Variant:
	# 来自 CEF 线程，UI 操作需 defer
	_log_add("← JS 调用: " + method_name)
	match method_name:
		"hello":
			return "Hello from GDScript! 你好，世界！"
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
			if arr and arr.size() > 0: _browser.EvalJs(str(arr[0]))
			return "eval started"
	return "unknown method: " + method_name

# ── Eval 按钮 ──

func _on_eval(code: String) -> void:
	_log_add("→ 计算 " + code)
	_browser.EvalJs(code)

func _on_custom_eval() -> void:
	var code = _custom_code.text.strip_edges()
	if code.is_empty(): code = "1 + 2 + 3"; _custom_code.text = code
	_log_add("→ 自定义计算 " + code)
	_browser.EvalJs(code)

func _on_eval_completed(result: String, error: String) -> void:
	if error and not error.is_empty(): _log_add("✗ " + error)
	else: _log_add("← " + result)

func _on_bridge_request(type: String, payload: String, cb_id: String) -> void:
	_log_add("← 桥接请求: " + type)

func _clear_log() -> void: _log.clear()

func _log_add(msg: String) -> void:
	# 可能来自 CEF 线程，用 call_deferred 保证线程安全
	var time = Time.get_time_string_from_system()
	call_deferred("_log_add_deferred", time, msg)

func _log_add_deferred(time: String, msg: String) -> void:
	if _log == null: return
	_log.add_text("[" + time + "] " + msg + "\n")
	_log.scroll_to_line(9999)
