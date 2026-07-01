extends Control

var _browser
var _url_input: LineEdit
var _go_button: Button
var _back_button: Button
var _forward_button: Button
var _reload_button: Button
var _open_dev_button: Button
var _status_label: Label

func _ready() -> void:
    _browser = $CefGlueControl
    _url_input = $Toolbar/UrlInput
    _go_button = $Toolbar/GoButton
    _back_button = $Toolbar/BackButton
    _forward_button = $Toolbar/ForwardButton
    _reload_button = $Toolbar/ReloadButton
    _open_dev_button = $Toolbar/OpenDevButton
    _status_label = $StatusBar/StatusLabel

    _browser.InitialUrl = "https://www.bing.com"
    _browser.FrameRate = 120
    _browser.Transparent = true

    _browser.BrowserInitialized.connect(_on_browser_initialized)
    _browser.AddressChanged.connect(_on_address_changed)
    _browser.LoadStart.connect(_on_load_start)
    _browser.LoadEnd.connect(_on_load_end)
    _browser.LoadError.connect(_on_load_error)

    _go_button.pressed.connect(_on_go_pressed)
    _back_button.pressed.connect(_on_back_pressed)
    _forward_button.pressed.connect(_on_forward_pressed)
    _reload_button.pressed.connect(_on_reload_pressed)
    _open_dev_button.pressed.connect(_on_open_dev_pressed)
    _url_input.text_submitted.connect(_on_url_submitted)

func _on_browser_initialized() -> void:
    _status_label.text = "Ready"

func _on_address_changed(url: String) -> void:
    _url_input.text = url

func _on_load_start() -> void:
    _status_label.text = "Loading..."

func _on_load_end() -> void:
    _status_label.text = "Done"

func _on_load_error(error_text: String, failed_url: String) -> void:
    _status_label.text = "Error: " + error_text

func _on_back_pressed() -> void:
    _browser.GoBack()

func _on_forward_pressed() -> void:
    _browser.GoForward()

func _on_reload_pressed() -> void:
    _browser.Reload()

func _on_open_dev_pressed() -> void:
    _browser.ShowDeveloperTools()

func _on_go_pressed() -> void:
    _navigate_to_url()

func _on_url_submitted(text: String) -> void:
    _navigate_to_url()

func _navigate_to_url() -> void:
    var url = _url_input.text.strip_edges()
    if url.is_empty():
        return

    if not url.begins_with("http://") and not url.begins_with("https://") and not url.begins_with("about:"):
        url = "https://" + url

    _browser.NavigateToUrl(url)
