extends Control

# ── Toolbar ──
var _tab_container: TabContainer
var _url_input: LineEdit
var _go_button: Button
var _back_button: Button
var _forward_button: Button
var _reload_button: Button
var _add_tab_button: Button
var _osr_toggle_button: Button
var _close_tab_button: Button
var _open_dev_button: Button
var _status_label: Label
var _tab_counter = 2

# ── OSR toggle ──
var _osr_mode := false

# ── Find-in-page ──
var _search_bar: Panel
var _search_input: LineEdit
var _search_prev: Button
var _search_next: Button
var _search_close: Button
var _search_match_count: Label
var _search_visible := false


func _ready() -> void:
	_tab_container = $TabContainer
	_url_input = $Toolbar/ToolbarHBox/UrlInput
	_go_button = $Toolbar/ToolbarHBox/GoButton
	_back_button = $Toolbar/ToolbarHBox/BackButton
	_forward_button = $Toolbar/ToolbarHBox/ForwardButton
	_reload_button = $Toolbar/ToolbarHBox/ReloadButton
	_add_tab_button = $Toolbar/ToolbarHBox/AddTabButton
	_osr_toggle_button = $Toolbar/ToolbarHBox/OsrToggleButton
	_close_tab_button = $Toolbar/ToolbarHBox/CloseTabButton
	_open_dev_button = $Toolbar/ToolbarHBox/OpenDevButton
	_status_label = $StatusBar/StatusLabel

	# Search bar
	_search_bar = $SearchBar
	_search_input = $SearchBar/SearchHBox/SearchInput
	_search_prev = $SearchBar/SearchHBox/SearchPrev
	_search_next = $SearchBar/SearchHBox/SearchNext
	_search_close = $SearchBar/SearchHBox/SearchClose
	_search_match_count = $SearchBar/SearchHBox/SearchMatchCount

	_go_button.pressed.connect(_on_go_pressed)
	_back_button.pressed.connect(_on_back_pressed)
	_forward_button.pressed.connect(_on_forward_pressed)
	_reload_button.pressed.connect(_on_reload_pressed)
	_add_tab_button.pressed.connect(_on_add_tab_pressed)
	_osr_toggle_button.pressed.connect(_on_osr_toggle_pressed)
	_close_tab_button.pressed.connect(_on_close_tab_pressed)
	_open_dev_button.pressed.connect(_on_open_dev_pressed)
	_url_input.text_submitted.connect(_on_url_submitted)

	_tab_container.tab_changed.connect(_on_tab_changed)

	# Search bar signals
	_search_input.text_changed.connect(_on_search_text_changed)
	_search_input.text_submitted.connect(_on_search_submitted)
	_search_prev.pressed.connect(_on_search_prev)
	_search_next.pressed.connect(_on_search_next)
	_search_close.pressed.connect(_on_search_close)

	# Connect existing tabs
	for c in _tab_container.get_children():
		if c is CefGlueControl:
			_connect_tab(c)

	_update_url_bar()
	_update_osr_button_state()
	_apply_theme()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F and event.ctrl_pressed:
			get_viewport().set_input_as_handled()
			_toggle_search_bar()
		if event.keycode == KEY_ESCAPE and _search_visible:
			get_viewport().set_input_as_handled()
			_hide_search_bar()


# ══════════════════════════════════════════════════════════════
#  Tab management
# ══════════════════════════════════════════════════════════════

func _connect_tab(cef: CefGlueControl) -> void:
	cef.browser_initialized.connect(_on_browser_initialized)
	cef.address_changed.connect(_on_address_changed)
	cef.title_changed.connect(_on_title_changed)
	cef.load_start.connect(_on_load_start)
	cef.load_end.connect(_on_load_end)
	cef.load_error.connect(_on_load_error)
	cef.new_window_requested.connect(_on_new_window_requested)
	cef.find_result.connect(_on_find_result)


func get_current_browser() -> CefGlueControl:
	var tab = _tab_container.get_current_tab_control()
	return tab as CefGlueControl


func _on_tab_changed(_index: int) -> void:
	_update_url_bar()
	_status_label.text = "Ready" if get_current_browser() else ""
	if _search_visible:
		_hide_search_bar()


func _update_url_bar() -> void:
	var cef = get_current_browser()
	if cef:
		_url_input.text = cef.address


func _on_new_window_requested(url: String, _is_new_window: bool) -> void:
	if url.is_empty(): return
	call_deferred("_add_tab_with_url", url)


func _add_tab_with_url(url: String) -> void:
	_tab_counter += 1
	var tab = CefGlueControl.new()
	tab.name = "Tab%d" % _tab_counter
	tab.frame_rate = 120
	tab.initial_url = url
	tab.mode = 0 if _osr_mode else 1  # 0=OSR, 1=EmbeddedWindow
	tab.transparent = _osr_mode
	tab.context_menu_enabled = _osr_mode
	tab.open_popup_in_current_browser = false
	tab.sync_cursor = true
	_connect_tab(tab)
	_tab_container.add_child(tab)
	_tab_container.current_tab = _tab_container.get_tab_count() - 1
	_status_label.text = "OSR mode (transparent)" if _osr_mode else "EmbeddedWindow mode"


# ══════════════════════════════════════════════════════════════
#  Toolbar handlers
# ══════════════════════════════════════════════════════════════

func _on_add_tab_pressed() -> void:
	_add_tab_with_url("https://www.bing.com")


func _on_osr_toggle_pressed() -> void:
	_osr_mode = not _osr_mode
	_update_osr_button_state()
	_status_label.text = "OSR mode ON" if _osr_mode else "OSR mode OFF"


func _update_osr_button_state() -> void:
	if _osr_mode:
		_osr_toggle_button.add_theme_color_override("font_color", Color(0.25, 0.50, 1.0))
	else:
		_osr_toggle_button.add_theme_color_override("font_color", Color(0.6, 0.6, 0.65))


func _on_close_tab_pressed() -> void:
	var tab = _tab_container.get_current_tab_control()
	if not tab: return
	tab.queue_free()
	if _tab_container.get_tab_count() == 0:
		get_tree().quit()


# ══════════════════════════════════════════════════════════════
#  Find-in-page
# ══════════════════════════════════════════════════════════════

func _toggle_search_bar() -> void:
	if _search_visible:
		_hide_search_bar()
	else:
		_show_search_bar()


func _show_search_bar() -> void:
	_search_visible = true
	_search_bar.visible = true
	_search_bar.offset_top = -52
	_search_bar.offset_bottom = -24
	_search_input.grab_focus()
	_search_input.select_all()


func _hide_search_bar() -> void:
	_search_visible = false
	_search_bar.visible = false
	_search_bar.offset_top = -24
	_search_bar.offset_bottom = -24
	var cef = get_current_browser()
	if cef:
		cef.stop_finding(true)
	_search_match_count.text = "0/0"


func _on_search_text_changed(text: String) -> void:
	var cef = get_current_browser()
	if not cef: return
	if text.is_empty():
		cef.stop_finding(true)
		_search_match_count.text = "0/0"
		return
	cef.find(text, true, false, false)


func _on_search_submitted(text: String) -> void:
	if text.is_empty(): return
	var cef = get_current_browser()
	if cef:
		cef.find(text, true, false, true)


func _on_search_next() -> void:
	var text = _search_input.text
	if text.is_empty(): return
	var cef = get_current_browser()
	if cef:
		cef.find(text, true, false, true)


func _on_search_prev() -> void:
	var text = _search_input.text
	if text.is_empty(): return
	var cef = get_current_browser()
	if cef:
		cef.find(text, false, false, true)


func _on_search_close() -> void:
	_hide_search_bar()


func _on_find_result(_identifier: int, count: int, active_match_ordinal: int, final_update: bool) -> void:
	if not final_update: return
	if count > 0:
		_search_match_count.text = "%d/%d" % [active_match_ordinal, count]
	else:
		_search_match_count.text = "0/0"


# ══════════════════════════════════════════════════════════════
#  CEF callbacks
# ══════════════════════════════════════════════════════════════

func _on_browser_initialized() -> void:
	_status_label.text = "Ready"


func _on_address_changed(url: String) -> void:
	_url_input.text = url


func _on_title_changed(title: String) -> void:
	var cef = get_current_browser()
	if cef and not title.is_empty():
		# 更新标签页标题（TabContainer 的标签文字）
		var idx = cef.get_index()
		var tab_title = title.substr(0, 20) + "…" if title.length() > 20 else title
		_tab_container.set_tab_title(idx, tab_title)


func _on_load_start() -> void:
	_status_label.text = "Loading..."


func _on_load_end() -> void:
	_status_label.text = "Done"


func _on_load_error(error_text: String, _failed_url: String) -> void:
	_status_label.text = "Error: " + error_text


# ══════════════════════════════════════════════════════════════
#  Toolbar actions
# ══════════════════════════════════════════════════════════════

func _on_back_pressed() -> void:
	var cef = get_current_browser()
	if cef: cef.go_back()


func _on_forward_pressed() -> void:
	var cef = get_current_browser()
	if cef: cef.go_forward()


func _on_reload_pressed() -> void:
	var cef = get_current_browser()
	if cef: cef.reload()


func _on_open_dev_pressed() -> void:
	var cef = get_current_browser()
	if cef: cef.show_developer_tools()


func _on_go_pressed() -> void:
	_navigate()


func _on_url_submitted(_text: String) -> void:
	_navigate()


func _navigate() -> void:
	var url = _url_input.text.strip_edges()
	if url.is_empty(): return
	if not url.begins_with("http://") and not url.begins_with("https://") and not url.begins_with("about:"):
		url = "https://" + url
	var cef = get_current_browser()
	if cef: cef.navigate_to_url(url)


# ══════════════════════════════════════════════════════════════
#  Dark theme
# ══════════════════════════════════════════════════════════════

func _apply_theme() -> void:
	var bg_dark = Color(0.12, 0.12, 0.13)
	var bg_medium = Color(0.16, 0.16, 0.17)
	var bg_light = Color(0.20, 0.20, 0.22)
	var accent = Color(0.25, 0.50, 1.0)
	var text_primary = Color(0.92, 0.92, 0.95)
	var text_secondary = Color(0.60, 0.60, 0.65)
	var border_color = Color(0.25, 0.25, 0.28)

	# ── Toolbar ──
	var toolbar_panel = $Toolbar
	var toolbar_bg = StyleBoxFlat.new()
	toolbar_bg.bg_color = bg_dark
	toolbar_bg.content_margin_left = 6
	toolbar_bg.content_margin_right = 6
	toolbar_panel.add_theme_stylebox_override("panel", toolbar_bg)

	# ── Status bar ──
	var status_panel = $StatusBar
	var status_bg = StyleBoxFlat.new()
	status_bg.bg_color = bg_dark
	status_panel.add_theme_stylebox_override("panel", status_bg)

	_status_label.add_theme_color_override("font_color", text_secondary)
	_status_label.add_theme_font_size_override("font_size", 12)

	# ── Search bar ──
	var search_panel = $SearchBar
	var search_bg = StyleBoxFlat.new()
	search_bg.bg_color = Color(0.14, 0.14, 0.15)
	search_bg.border_width_top = 1
	search_bg.border_width_bottom = 1
	search_bg.border_color = border_color
	search_panel.add_theme_stylebox_override("panel", search_bg)

	var search_label = $SearchBar/SearchHBox/SearchLabel
	search_label.add_theme_color_override("font_color", text_secondary)
	search_label.add_theme_font_size_override("font_size", 12)

	_search_match_count.add_theme_color_override("font_color", Color(0.8, 0.8, 0.3))
	_search_match_count.add_theme_font_size_override("font_size", 12)

	# ── TabContainer ──
	var tab_bg = StyleBoxFlat.new()
	tab_bg.bg_color = bg_medium
	_tab_container.add_theme_stylebox_override("panel", tab_bg)

	_tab_container.add_theme_color_override("font_color", text_secondary)
	_tab_container.add_theme_color_override("font_selected_color", text_primary)
	_tab_container.add_theme_color_override("font_hovered_color", Color(0.8, 0.8, 0.9))

	# ── Button theme ──
	var btn_normal = StyleBoxFlat.new()
	btn_normal.bg_color = Color(0, 0, 0, 0)
	btn_normal.content_margin_left = 6
	btn_normal.content_margin_right = 6
	btn_normal.content_margin_top = 4
	btn_normal.content_margin_bottom = 4

	var btn_hover = StyleBoxFlat.new()
	btn_hover.bg_color = bg_light
	btn_hover.corner_radius_top_left = 4
	btn_hover.corner_radius_top_right = 4
	btn_hover.corner_radius_bottom_left = 4
	btn_hover.corner_radius_bottom_right = 4
	btn_hover.content_margin_left = 6
	btn_hover.content_margin_right = 6
	btn_hover.content_margin_top = 4
	btn_hover.content_margin_bottom = 4

	var btn_pressed = StyleBoxFlat.new()
	btn_pressed.bg_color = Color(0.28, 0.28, 0.30)
	btn_pressed.corner_radius_top_left = 4
	btn_pressed.corner_radius_top_right = 4
	btn_pressed.corner_radius_bottom_left = 4
	btn_pressed.corner_radius_bottom_right = 4
	btn_pressed.content_margin_left = 6
	btn_pressed.content_margin_right = 6
	btn_pressed.content_margin_top = 4
	btn_pressed.content_margin_bottom = 4

	var all_buttons = [
		_back_button, _forward_button, _reload_button,
		_add_tab_button, _osr_toggle_button, _close_tab_button,
		_go_button, _open_dev_button,
		_search_prev, _search_next, _search_close
	]

	for btn in all_buttons:
		if not btn: continue
		btn.add_theme_stylebox_override("normal", btn_normal)
		btn.add_theme_stylebox_override("hover", btn_hover)
		btn.add_theme_stylebox_override("pressed", btn_pressed)
		btn.add_theme_color_override("font_color", text_primary)
		btn.add_theme_color_override("font_hover_color", text_primary)
		btn.add_theme_color_override("font_pressed_color", text_primary)
		btn.add_theme_font_size_override("font_size", 12)

	# ── LineEdit (URL bar + search) ──
	var url_bg = StyleBoxFlat.new()
	url_bg.bg_color = Color(0.08, 0.08, 0.09)
	url_bg.corner_radius_top_left = 4
	url_bg.corner_radius_top_right = 4
	url_bg.corner_radius_bottom_left = 4
	url_bg.corner_radius_bottom_right = 4
	url_bg.content_margin_left = 10
	url_bg.content_margin_right = 10
	url_bg.content_margin_top = 4
	url_bg.content_margin_bottom = 4

	var url_focused = StyleBoxFlat.new()
	url_focused.bg_color = Color(0.08, 0.08, 0.09)
	url_focused.border_width_bottom = 1
	url_focused.border_color = accent
	url_focused.corner_radius_top_left = 4
	url_focused.corner_radius_top_right = 4
	url_focused.corner_radius_bottom_left = 4
	url_focused.corner_radius_bottom_right = 4
	url_focused.content_margin_left = 10
	url_focused.content_margin_right = 10
	url_focused.content_margin_top = 4
	url_focused.content_margin_bottom = 4

	var url_inputs = [_url_input, _search_input]
	for input in url_inputs:
		if not input: continue
		input.add_theme_stylebox_override("normal", url_bg)
		input.add_theme_stylebox_override("focus", url_focused)
		input.add_theme_color_override("font_color", text_primary)
		input.add_theme_color_override("placeholder_color", text_secondary)
		input.add_theme_color_override("caret_color", accent)
		input.add_theme_font_size_override("font_size", 13)