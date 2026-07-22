# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md) | [English](README_EN.md)

## Features

- **Dual render mode**: OSR (off-screen rendering with alpha transparency) and EmbeddedWindow (native child window, higher performance)
- **Inspector property grouping**: Browser Settings / Feature Toggles / Embedded Mode
- **Dynamic property visibility**: OSR mode hides embedded-mode properties, EmbeddedWindow hides OSR-specific properties
- **Cross-platform embedded window**: Windows (Win32), Linux (X11), macOS (Cocoa)
- **Keyboard event forwarding**: Forward browser keyboard events to Godot in EmbeddedWindow mode
- **GPU hardware acceleration**
- **IME support for Chinese/Japanese/Korean input**: JS focus watcher auto-detects input focus, drives IME activation/deactivation
- **Right-click context menu**: Godot PopupMenu in OSR mode, fully customizable
- **Popup handling**
- **Complete keyboard and mouse support**
- **In-page search**: `Find()` / `StopFinding()` methods + `FindResult` event
- **JS ↔ C# bridge** via RegisterJavascriptObject (CEF IPC, no iframe needed)
- **GDScript bridge** via RegisterJsHandler (Callable-based, for GDExtension)
- **Engine-agnostic JS API**: window.__hostBridge / window.__hostEvents / window.__hostFocus — write once, works across Godot, Unreal, or any CEF host

## Quick Start

### C# (Plugin)

Add a CefGlueControl node to your scene:

`csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;        // OSR (transparent) or EmbeddedWindow
browser.Transparent = true;           // OSR only
AddChild(browser);
`

### GDScript (GDExtension)

`gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow

# Connect to signals (GDExtension uses snake_case names)
browser.browser_initialized.connect(_on_ready)
browser.address_changed.connect(_on_address_changed)
browser.load_start.connect(_on_loading)
browser.load_end.connect(_on_done)
browser.load_error.connect(_on_error)
`

## Requirements

- **Godot Engine**: 4.6.0 or later (with .NET/Mono support)
- **.NET SDK**: 8.0 or later
- **Windows/Linux/macOS**: x64 architecture (ARM64 also supported)

## Build

### Plugin (C#, Godot.NET.Sdk)

```bash
# Build (compile check)
dotnet build plugin/GDCefGlue.csproj

# Publish
dotnet publish plugin/GDCefGlue.csproj -c Release
```

CEF files are copied automatically during build.

### Extension (GDExtension, NativeAOT)

```bash
# Build (compile check only, NOT for GDExtension)
dotnet build extension/GDCefGlueExtension.csproj

# AOT publish (required for GDExtension)
dotnet publish extension/GDCefGlueExtension.csproj -c Release -r win-x64
```

**AOT output:**
- Native DLL: `extension/bin/Release/net10.0/win-x64/native/GDCefGlueExtension.dll`
- Publish folder: `extension/bin/Release/net10.0/win-x64/publish/`

> **Note:** Extension must use `dotnet publish -r <RID>` for AOT compilation. `dotnet build` only produces managed assemblies, not loadable by GDExtension. Supported RIDs: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.

## NuGet Packages

The CefGlue NuGet packages are published on [GitHub Releases](https://github.com/youfch/CefGlue/releases) (not on NuGet.org). Download the required `.nupkg` files and set up a local feed.

### Setup

1. **Download all** `.nupkg` files from [GitHub Releases](https://github.com/youfch/CefGlue/releases/tag/v149.7827.156) — `CefGlue.BrowserProcess.runtime.jit` is a meta-package, its dependencies must be resolved locally.

2. **Place** them in a local folder, e.g. `./nuget-feed/` in your project.

3. **Create** a `nuget.config` in your project root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-cefglue" value="./nuget-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

4. **Add** the packages to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="CefGlue.BrowserProcess.runtime.jit" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

All CEF files are copied automatically during build. No manual file copying needed.

> **Note:** `CefGlue.BrowserProcess.runtime.jit` provides the CEF browser subprocess.
> For cross-platform builds (Linux/macOS), add the corresponding `cef.redist.*` packages.
> If you have a local NuGet cache, you can also use `dotnet nuget add source` to register the feed.

## Project Structure

`
addons/GCefGlue/                    ← Plugin source code
├── CefGlueControl.cs               Skeleton (enum, fields, constructor)
├── CefGlueControl.Properties.cs    Export properties, static properties, events
├── CefGlueControl.Initialization.cs CEF init, lifecycle, browser creation
├── CefGlueControl.Rendering.cs     OSR paint, _Process, _Draw, cursor
├── CefGlueControl.Input.cs         Input forwarding, IME, _Notification
├── CefGlueControl.Bridge.cs        JS bridge, IPC, deserialization
├── CefGlueControl.Navigation.cs    Navigation, DevTools, CEF callbacks
├── CefGlueControl.Inspector.cs     Inspector property visibility (_ValidateProperty)
├── CefGlueControl.Events.cs        ForwardInputEvents event forwarding
├── CefGlueControl.Embedded.cs      Embedded window mode
├── CefGlueControl.ContextMenu.cs   OSR context menu (PopupMenu)
├── CefGlueControl.Cookies.cs       Cookie management
├── CefInitializer.cs               CEF initialization
├── Handlers/                       CEF handlers
│   ├── GodotCefApp.cs
│   ├── GodotCefClient.cs
│   ├── GodotDisplayHandler.cs
│   ├── GodotLifeSpanHandler.cs
│   ├── GodotLoadHandler.cs
│   ├── GodotRenderHandler.cs
│   ├── GodotRequestHandler.cs
│   ├── GodotContextMenuHandler.cs
│   ├── GodotFocusHandler.cs
│   ├── GodotFindHandler.cs
│   ├── GodotPermissionHandler.cs
│   └── GodotBrowserProcessHandler.cs
└── Platform/                       Cross-platform native API
    ├── NativeWindowMethods.cs      Platform abstraction layer
    ├── X11Methods.cs               Linux X11 P/Invoke
    └── MacMethods.cs               macOS Cocoa P/Invoke
`

## CefGlueControl Properties

| Property | Type | Default | Group | Description |
|----------|------|---------|-------|-------------|
| InitialUrl | string | "about:blank" | Browser Settings | URL to load when browser is created |
| Mode | RenderMode | OSR | Browser Settings | Render mode: OSR / EmbeddedWindow |
| FrameRate | int | 60 | Browser Settings | Browser frame rate, 1-360 |
| Transparent | bool | false | Browser Settings | Enable transparent background (OSR only) |
| CacheDirectory | string | "user://cef_cache" | Browser Settings | CEF cache directory |
| GpuAcceleration | bool | true | Feature Toggles | Enable GPU hardware acceleration |
| OpenPopupInCurrentBrowser | bool | false | Feature Toggles | Open popups in current browser |
| EnableMediaStream | bool | false | Feature Toggles | Enable media stream (microphone/camera) |
| SyncCursor | bool | false | Feature Toggles | Sync cursor with web content (OSR only) |
| ContextMenuEnabled | bool | true | Feature Toggles | Enable right-click context menu (OSR only) |
| ForwardInputEvents | bool | false | Embedded Mode | Forward browser events to Godot (EmbeddedWindow only) |

### Dynamic Property Visibility

| Mode | Visible | Hidden |
|------|---------|--------|
| OSR | SyncCursor, Transparent, ContextMenuEnabled | ForwardInputEvents, "Embedded Mode" group |
| EmbeddedWindow | ForwardInputEvents, "Embedded Mode" group | SyncCursor, ContextMenuEnabled |

### RenderMode Enum

| Value | Description |
|-------|-------------|
| OSR (0) | Off-screen rendering. CEF renders to memory → Godot texture. **Supports transparency**. Cross-platform (Windows/Linux/macOS) |
| EmbeddedWindow (1) | Embedded native child window. CEF renders directly to OS window. **Better performance** (video/WebGL), **no transparency**. Cross-platform: Windows (Win32), Linux (X11), macOS (Cocoa) |

### ForwardInputEvents

When Mode = EmbeddedWindow and ForwardInputEvents = true, browser mouse/keyboard events are forwarded to Godot via IPC:

`
JS events → window.__hostEvents.forward(payload) → CEF IPC → C# → viewport.PushInput()
`

### Static Properties

| Property | Type | Description |
|----------|------|-------------|
| UseGpuAcceleration | bool | Global GPU acceleration, must be set before CEF initialization |
| UseTransparent | bool | Global transparency setting, must be set before CEF initialization |
| ActiveRenderMode | RenderMode | Global rendering mode, must be set before CEF initialization |

### Read-only Properties

| Property | Type | Description |
|----------|------|-------------|
| Address | string | Current page URL |
| IsBrowserInitialized | bool | Whether the browser is initialized |
| IsLoading | bool | Whether the page is loading |
| Title | string | Current page title |

## CefGlueControl Methods

### C# (Plugin)

| Method | Returns | Description |
|--------|---------|-------------|
| GoBack() | void | Navigate back |
| GoForward() | void | Navigate forward |
| NavigateToUrl(string url) | void | Navigate to URL |
| Reload(bool ignoreCache = false) | void | Reload page |
| ExecuteJavaScript(string code, ...) | void | Execute JavaScript |
| EvaluateJavaScript<T>(string code, ...) | Task<T> | Execute JS and return result (async) |
| EvalJs(string code) | void | Async JS eval, result via eval_completed signal |
| ShowDeveloperTools() | void | Open DevTools |
| CloseDeveloperTools() | void | Close DevTools |
| Find(string text, bool forward, bool matchCase, bool findNext) | void | In-page search |
| StopFinding(bool clearSelection) | void | Stop search |
| RegisterJavascriptObject(object target, string name) | void | Register C# object callable from JS |
| UnregisterJavascriptObject(string name) | void | Unregister object |
| SendToJs(string json) | void | Push message to JS |
| SendResponse(string cbId, string json) | void | Reply to bridge request |

### GDScript (GDExtension)

| Method | Returns | Description |
|--------|---------|-------------|
| go_back() | void | Navigate back |
| go_forward() | void | Navigate forward |
| navigate_to_url(url: String) | void | Navigate to URL |
| reload(ignore_cache: bool = false) | void | Reload page |
| execute_javascript(code: String, url: String = "about:blank", line: int = 1) | void | Execute JavaScript |
| eval_js(code: String) | void | Async JS eval, result via eval_completed signal |
| show_developer_tools() | void | Open DevTools |
| close_developer_tools() | void | Close DevTools |
| find(search_text: String, forward: bool = true, match_case: bool = false, find_next: bool = false) | void | In-page search |
| stop_finding(clear_selection: bool = true) | void | Stop search |
| register_js_handler(name: String, handler: Callable, methods: String = "[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]") | void | Register GDScript handler callable from JS |
| unregister_js_handler(name: String) | void | Unregister handler |
| send_to_js(json: String) | void | Push message to JS |
| send_response(cb_id: String, json: String) | void | Reply to bridge request |

## JS ↔ C# Bridge

### C# (Plugin): RegisterJavascriptObject (Recommended) ✅

Register a C# object so JS can call its methods via V8 IPC (CEF SendProcessMessage).

`csharp
browser.RegisterJavascriptObject(new MyBridge(), "myBridge");
`

**JS call (returns Promise):**
`javascript
window.myBridge.hello().then(function(result) {
    console.log(result);
}).catch(function(err) {
    console.error(err);
});
`

**C# → JS: evaluate and get result:**
`csharp
var title = await browser.EvaluateJavaScript<string>("document.title");
`

**C# → JS: push message (JS receives via window.__hostBridge._onMessage):**
`csharp
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");
`

### GDScript (GDExtension): RegisterJsHandler

`gdscript
browser.RegisterJsHandler("dotnetBridge", Callable(self, "_on_js_call"))
`

`gdscript
func _on_js_call(method_name: String, args_json: String) -> Variant:
    match method_name:
        "hello":
            return "Hello from GDScript!"
        "add":
            var arr = JSON.parse_string(args_json) as Array
            return int(arr[0]) + int(arr[1])
`

### JS API Reference

| Object | Method | Description |
|--------|--------|-------------|
| window.__hostBridge | ._onMessage(msg) | Receive push messages from host (C#/GDScript) |
| window.__hostBridge | ._onResponse(cbId, json) | Receive response from host |
| window.__hostEvents | .forward(payload) | Forward input events to host (EmbeddedWindow mode) |
| window.__hostFocus | .onInputFocusChanged(bool) | Notify host of input focus changes (IME drive) |

The __hostBridge / __hostEvents / __hostFocus naming is engine-agnostic — the same HTML page works with Godot, Unreal, or any CEF host without modification.

### API Overview

| API | Platform | Direction | Description |
|-----|----------|-----------|-------------|
| RegisterJavascriptObject(target, name) | Plugin | C# → JS | Register object callable from JS |
| RegisterJsHandler(name, Callable, methods) | GDExtension | GDScript → JS | Register GDScript handler |
| EvaluateJavaScript<T>(code, ...) | Plugin | C# → JS | Execute JS and return result |
| EvalJs(code) | Both | GDScript/C# → JS | Async JS eval via signal |
| SendToJs(json) | Both | C#/GDScript → JS | Push message to JS |
| SendResponse(cbId, json) | Both | C#/GDScript → JS | Reply to bridge request |
| BridgeRequest event | Both | JS → C# | Bridge request entry |
| ridge_request signal | GDExtension | JS → GDScript | Bridge request signal |

### IME Focus Watcher

A `__hostFocus` V8 object + JS focus watcher script is auto-injected on page load. It detects `focusin`/`focusout` events on editable elements (`<input>`, `<textarea>`, `contentEditable`) and drives IME activation/deactivation:

```javascript
// Auto-called by injected script on focus change
window.__hostFocus.onInputFocusChanged(true);   // Input focused → activate IME
window.__hostFocus.onInputFocusChanged(false);  // Input blurred → deactivate IME
```

No manual IME management needed. Works in both OSR and EmbeddedWindow modes.

## Events / Signals

### C# Events (Plugin)

C# events use **PascalCase** naming, subscribe via `+=`:

| Event | Parameters | Description |
|-------|-----------|-------------|
| `BrowserInitialized` | `Action` | Browser initialization complete |
| `AddressChanged` | `AddressChangedEventHandler` | Current page URL changed |
| `TitleChanged` | `TitleChangedEventHandler` | Page title changed |
| `LoadStart` | `LoadStartEventHandler` | Page starts loading |
| `LoadEnd` | `LoadEndEventHandler` | Page finishes loading |
| `LoadError` | `LoadErrorEventHandler` | Page failed to load |
| `BridgeRequest` | `Action<string, string, string>` | JS → C# bridge request (type, payload, cbId) |
| `NewWindowRequested` | `Action<string, bool>` | New window/tab requested |
| `FindResult` | `Action<int, int, int, bool>` | In-page search result (identifier, count, activeMatchOrdinal, finalUpdate) |
| `BeforeContextMenu` | `Action<ContextMenuModel, ContextMenuParams>` | Context menu about to show (customizable) |
| `ContextMenuCommand` | `Func<int, ContextMenuParams, CefEventFlags, bool>` | Context menu command selected |
| `CookiesVisited` | `Action<List<CookieInfo>>` | Cookie enumeration complete |
| `SetCookieCompleted` | `Action<bool>` | SetCookie complete |
| `DeleteCookiesCompleted` | `Action<int>` | DeleteCookies complete |

### GDScript Signals (GDExtension)

GDScript signals use **snake_case** naming, subscribe via `.connect()`:

| Signal | Parameters | Description |
|--------|-----------|-------------|
| `browser_initialized` | — | Browser initialization complete |
| `address_changed` | `url: String` | Current page URL changed |
| `title_changed` | `title: String` | Page title changed |
| `load_start` | — | Page starts loading |
| `load_end` | — | Page finishes loading |
| `load_error` | `errorText: String, failedUrl: String` | Page failed to load |
| `eval_completed` | `result: String, error: String` | EvalJs result |
| `bridge_request` | `type: String, payload: String, cbId: String` | JS → GDScript bridge request |
| `new_window_requested` | `url: String, isNewWindow: bool` | New window/tab requested |
| `find_result` | `identifier: int, count: int, activeMatchOrdinal: int, finalUpdate: bool` | In-page search result |  

## Demo

### C# Plugin (Godot .NET project)

Open `plugin/` in Godot 4.6+:

| Demo | Path | Description |
|------|------|-------------|
| Multi-tab browser | `plugin/demo/browser/Browser.tscn` | Full browser UI with tabs, navigation, DevTools |
| IPC bridge | `plugin/demo/ipc/IpcDemo.tscn` | JS ↔ C# bridge communication demo |

### GDExtension (Native AOT)

Open `test/GDExtensionGame/` in Godot 4.6+:

| Demo | Path | Description |
|------|------|-------------|
| Multi-tab browser | `test/GDExtensionGame/demo/browser/Browser.tscn` | GDScript version of browser demo |
| IPC bridge | `test/GDExtensionGame/demo/ipc/IpcDemo.tscn` | JS ↔ GDScript bridge demo |

## Troubleshooting

### GPU Process Crashed
1. Ensure all CEF files are copied correctly
2. Try disabling GPU acceleration in the inspector

### Missing DLLs
1. Run dotnet restore
2. Clean and rebuild the solution

### Blank Page
1. Check if locales directory exists
2. Ensure esources.pak is present

## Known Issues

1. **Network Notification**: `WSALookupServiceBegin failed with: 10108` is a normal warning, does not affect functionality.
2. **JS Bridge S prefix**: CefGlue's serialization protocol prepends 'S' marker to strings. Automatically stripped.

## License

GDCefGlue is licensed under the MIT License.

Third-party dependencies:
- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
