extends Control

var _tab_container: TabContainer
var _url_input: LineEdit
var _go_button: Button
var _back_button: Button
var _forward_button: Button
var _reload_button: Button
var _add_tab_button: Button
var _close_tab_button: Button
var _open_dev_button: Button
var _status_label: Label
var _tab_counter = 2

func _ready() -> void:
	_tab_container = $TabContainer
	_url_input = $Toolbar/UrlInput
	_go_button = $Toolbar/GoButton
	_back_button = $Toolbar/BackButton
	_forward_button = $Toolbar/ForwardButton
	_reload_button = $Toolbar/ReloadButton
	_add_tab_button = $Toolbar/AddTabButton
	_close_tab_button = $Toolbar/CloseTabButton
	_open_dev_button = $Toolbar/OpenDevButton
	_status_label = $StatusBar/StatusLabel

	_go_button.pressed.connect(_on_go_pressed)
	_back_button.pressed.connect(_on_back_pressed)
	_forward_button.pressed.connect(_on_forward_pressed)
	_reload_button.pressed.connect(_on_reload_pressed)
	_add_tab_button.pressed.connect(_on_add_tab_pressed)
	_close_tab_button.pressed.connect(_on_close_tab_pressed)
	_open_dev_button.pressed.connect(_on_open_dev_pressed)
	_url_input.text_submitted.connect(_on_url_submitted)

	_tab_container.tab_changed.connect(_on_tab_changed)

	# Connect existing tabs
	for c in _tab_container.get_children():
		if c is CefGlueControl:
			_connect_tab(c)

	_update_url_bar()

func _connect_tab(cef: CefGlueControl) -> void:
	cef.BrowserInitialized.connect(_on_browser_initialized)
	cef.AddressChanged.connect(_on_address_changed)
	cef.TitleChanged.connect(_on_title_changed)
	cef.LoadStart.connect(_on_load_start)
	cef.LoadEnd.connect(_on_load_end)
	cef.LoadError.connect(_on_load_error)
	cef.NewWindowRequested.connect(_on_new_window_requested)

func get_current_browser() -> CefGlueControl:
	var tab = _tab_container.get_current_tab_control()
	return tab as CefGlueControl

func _on_tab_changed(_index: int) -> void:
	_update_url_bar()
	_status_label.text = "Ready" if get_current_browser() else ""

func _update_url_bar() -> void:
	var cef = get_current_browser()
	if cef: _url_input.text = cef.Address

func _on_new_window_requested(url: String, _is_new_window: bool) -> void:
	if url.is_empty(): return
	call_deferred("_add_tab_with_url", url)

func _add_tab_with_url(url: String) -> void:
	_tab_counter += 1
	var tab = CefGlueControl.new()
	tab.Name = "Tab%d" % _tab_counter
	tab.FrameRate = 120
	tab.InitialUrl = url
	tab.Mode = 1  # EmbeddedWindow
	tab.OpenPopupInCurrentBrowser = false
	tab.SyncCursor = true
	_connect_tab(tab)
	_tab_container.add_child(tab)
	_tab_container.current_tab = _tab_container.get_tab_count() - 1

func _on_add_tab_pressed() -> void:
	_add_tab_with_url("https://www.bing.com")

func _on_close_tab_pressed() -> void:
	var tab = _tab_container.get_current_tab_control()
	if not tab: return
	tab.queue_free()
	if _tab_container.get_tab_count() == 0:
		get_tree().quit()

func _on_browser_initialized() -> void: _status_label.text = "Ready"
func _on_address_changed(url: String) -> void: _url_input.text = url
func _on_title_changed(title: String) -> void:
	var cef = get_current_browser()
	if cef and not title.is_empty():
		cef.Name = title.substr(0, 20) + "…" if title.length() > 20 else title
func _on_load_start() -> void: _status_label.text = "Loading..."
func _on_load_end() -> void: _status_label.text = "Done"
func _on_load_error(error_text: String, _failed_url: String) -> void: _status_label.text = "Error: " + error_text

func _on_back_pressed() -> void: get_current_browser().GoBack()
func _on_forward_pressed() -> void: get_current_browser().GoForward()
func _on_reload_pressed() -> void: get_current_browser().Reload()
func _on_open_dev_pressed() -> void: get_current_browser().ShowDeveloperTools()
func _on_go_pressed() -> void: _navigate()
func _on_url_submitted(_text: String) -> void: _navigate()

func _navigate() -> void:
	var url = _url_input.text.strip_edges()
	if url.is_empty(): return
	if not url.begins_with("http://") and not url.begins_with("https://") and not url.begins_with("about:"):
		url = "https://" + url
	get_current_browser().NavigateToUrl(url)