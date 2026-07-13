extends Control

var _browser
var _url_input: LineEdit
var _go_button: Button
var _back_button: Button
var _forward_button: Button
var _reload_button: Button
var _open_dev_button: Button
var _status_label: Label
var _log: RichTextLabel
var _custom_code: LineEdit

func _ready() -> void:
	_browser = $CefGlueControl
	_url_input = $Toolbar/UrlInput
	_go_button = $Toolbar/GoButton
	_back_button = $Toolbar/BackButton
	_forward_button = $Toolbar/ForwardButton
	_reload_button = $Toolbar/ReloadButton
	_open_dev_button = $Toolbar/OpenDevButton
	_status_label = $StatusBar/StatusLabel
	_log = $LogPanel/Log
	_custom_code = $Toolbar/CustomCode

	_browser.InitialUrl = "https://www.bing.com"
	_browser.FrameRate = 120

	_browser.BrowserInitialized.connect(_on_browser_initialized)
	_browser.AddressChanged.connect(_on_address_changed)
	_browser.LoadStart.connect(_on_load_start)
	_browser.LoadEnd.connect(_on_load_end)
	_browser.LoadError.connect(_on_load_error)
	_browser.eval_completed.connect(_on_eval_completed)
	_browser.bridge_request.connect(_on_bridge_request)

	_go_button.pressed.connect(_on_go_pressed)
	_back_button.pressed.connect(_on_back_pressed)
	_forward_button.pressed.connect(_on_forward_pressed)
	_reload_button.pressed.connect(_on_reload_pressed)
	_open_dev_button.pressed.connect(_on_open_dev_pressed)
	_url_input.text_submitted.connect(_on_url_submitted)

	$Toolbar/BtnEvalTitle.pressed.connect(_on_eval_button.bind("document.title"))
	$Toolbar/BtnEvalUrl.pressed.connect(_on_eval_button.bind("location.href"))
	$Toolbar/BtnEvalMath.pressed.connect(_on_eval_button.bind("Math.PI * 2"))
	$Toolbar/BtnClearLog.pressed.connect(_clear_log)
	$Toolbar/BtnEvalCustom.pressed.connect(_on_custom_eval)

func _on_browser_initialized() -> void:
	_status_label.text = "Ready"
	_log_add("浏览器已就绪")

	# 注册 JS bridge 处理器
	_browser.RegisterJsHandler("dotnetBridge", Callable(self, "_on_js_call"))
	_log_add("已注册 dotnetBridge，JS 可通过 window.dotnetBridge.* 调用")

	# 注入 _godotBridge 辅助脚本
	_inject_bridge()

	# 加载测试 HTML
	_load_test_html()

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
	var file = FileAccess.open("res://demo/test.html", FileAccess.READ)
	if file == null:
		_log_add("❌ 找不到 test.html")
		return
	var html = file.get_as_text()
	_browser.Address = "data:text/html;charset=utf-8," + _uri_encode(html)
	_log_add("已加载测试页面")

static func _uri_encode(s: String) -> String:
	var hex = "0123456789ABCDEF"
	var result = ""
	var bytes = s.to_utf8_buffer()
	for b in bytes:
		var c = b as int
		if (c >= 0x30 and c <= 0x39) or (c >= 0x41 and c <= 0x5A) or (c >= 0x61 and c <= 0x7A) or c == 0x2D or c == 0x5F or c == 0x2E or c == 0x7E:
			result += char(c)
		else:
			result += "%" + hex[c >> 4] + hex[c & 0xF]
	return result

# ── JS Bridge 处理器 ──

func _on_js_call(method_name: String, args_json: String) -> Variant:
	_log_add("← JS 调用: " + method_name)
	match method_name:
		"hello":
			return "Hello from GDScript! 你好，世界！"
		"echo":
			var arr = JSON.parse_string(args_json) as Array
			if arr and arr.size() > 0:
				return "GDScript echoes: " + str(arr[0])
			return "GDScript echoes: (empty)"
		"add":
			var arr = JSON.parse_string(args_json) as Array
			if arr and arr.size() >= 2:
				return int(arr[0]) + int(arr[1])
			return 0
		"getVersion":
			return "GDCefGlue GDE 1.0 + CefGlue 149"
		"eval":
			var arr = JSON.parse_string(args_json) as Array
			if arr and arr.size() > 0:
				_browser.EvalJs(str(arr[0]))
				return "eval started"
			return "eval error: no code"
	return _log_add("⚠️ 未知方法: " + method_name)

# ── Eval 按钮 ──

func _on_eval_button(code: String) -> void:
	_log_add("→ 计算 " + code)
	_browser.EvalJs(code)

func _on_custom_eval() -> void:
	var code = _custom_code.text.strip_edges()
	if code.is_empty():
		code = "1 + 2 + 3"
		_custom_code.text = code
	_log_add("→ 自定义计算 " + code)
	_browser.EvalJs(code)

func _on_eval_completed(result: String, error: String) -> void:
	if error and not error.is_empty():
		_log_add("✗ " + error)
	else:
		_log_add("← " + result)

func _clear_log() -> void:
	_log.clear()

func _log_add(msg: String) -> void:
	if _log == null: return
	_log.add_text("[" + Time.get_time_string_from_system() + "] " + msg + "\n")
	_log.scroll_to_line(9999)

# ── Bridge 请求 ──

func _on_bridge_request(type: String, payload: String, cb_id: String) -> void:
	_log_add("← 桥接请求: " + type)

# ── 工具栏按钮 ──

func _on_back_pressed() -> void: _browser.GoBack()
func _on_forward_pressed() -> void: _browser.GoForward()
func _on_reload_pressed() -> void: _browser.Reload()
func _on_open_dev_pressed() -> void: _browser.ShowDeveloperTools()
func _on_go_pressed() -> void: _navigate_to_url()
func _on_url_submitted(text: String) -> void: _navigate_to_url()

func _navigate_to_url() -> void:
	var url = _url_input.text.strip_edges()
	if url.is_empty(): return
	if not url.begins_with("http://") and not url.begins_with("https://") and not url.begins_with("about:"):
		url = "https://" + url
	_browser.NavigateToUrl(url)

# ── CEF 回调 ──

func _on_address_changed(url: String) -> void: _url_input.text = url
func _on_load_start() -> void: _status_label.text = "Loading..."
func _on_load_end() -> void: _status_label.text = "Done"
func _on_load_error(error_text: String, failed_url: String) -> void: _status_label.text = "Error: " + error_text