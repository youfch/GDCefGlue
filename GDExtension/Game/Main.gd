extends Node

func _ready() -> void:
	var browser = $CefGlueControl
	browser.InitialUrl = "https://www.bing.com"
