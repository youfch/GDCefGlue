# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md) | [English](README_EN.md)

## Features

- **Dual render mode**: OSR (off-screen rendering with alpha transparency) and EmbeddedWindow (native child window, higher performance)
- **Inspector property grouping**: Browser Settings / Feature Toggles / Embedded Mode
- **Dynamic property visibility**: OSR mode hides embedded-mode properties, EmbeddedWindow hides OSR-specific properties
- **Cross-platform embedded window**: Windows (Win32), Linux (X11), macOS (Cocoa)
- **Keyboard event forwarding**: Forward browser keyboard events to Godot in EmbeddedWindow mode
- GPU hardware acceleration
- IME support for Chinese/Japanese/Korean input
- Popup handling
- Complete keyboard and mouse support
- **JS ↔ C# bridge** via RegisterJavascriptObject (CEF IPC, no iframe needed)
- **GDScript bridge** via RegisterJsHandler (Callable-based, for GDExtension)
- **Engine-agnostic JS API**: window.__hostBridge / window.__hostEvents — write once, works across Godot, Unreal, or any CEF host

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
var browser: CefGlueControl = 
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow

# Connect to signals
browser.BrowserInitialized.connect(_on_ready)
browser.AddressChanged.connect(_on_address_changed)
browser.LoadStart.connect(_on_loading)
browser.LoadEnd.connect(_on_done)
browser.LoadError.connect(_on_error)
`

## Requirements

- **Godot Engine**: 4.6.0 or later (with .NET/Mono support)
- **.NET SDK**: 8.0 or later
- **Windows/Linux/macOS**: x64 architecture (ARM64 also supported)

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
├── CefInitializer.cs               CEF initialization
├── Handlers/                       CEF handlers
│   ├── GodotCefApp.cs
│   ├── GodotCefClient.cs
│   ├── GodotDisplayHandler.cs
│   ├── GodotLifeSpanHandler.cs
│   ├── GodotLoadHandler.cs
│   ├── GodotRenderHandler.cs
│   ├── GodotRequestHandler.cs
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
| GpuAcceleration | bool | true | Feature Toggles | Enable GPU hardware acceleration |
| OpenPopupInCurrentBrowser | bool | true | Feature Toggles | Open popups in current browser |
| SyncCursor | bool | false | Feature Toggles | Sync cursor with web content (OSR only) |
| ForwardInputEvents | bool | false | Embedded Mode | Forward browser events to Godot (EmbeddedWindow only) |

### Dynamic Property Visibility

| Mode | Visible | Hidden |
|------|---------|--------|
| OSR | SyncCursor, Transparent | ForwardInputEvents, "Embedded Mode" group |
| EmbeddedWindow | ForwardInputEvents, "Embedded Mode" group | SyncCursor |

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

### Read-only Properties

| Property | Type | Description |
|----------|------|-------------|
| Address | string | Current page URL |
| IsBrowserInitialized | bool | Whether the browser is initialized |
| IsLoading | bool | Whether the page is loading |
| Title | string | Current page title |

## CefGlueControl Methods

| Method | Description |
|--------|-------------|
| GoBack() | Navigate back |
| GoForward() | Navigate forward |
| NavigateToUrl(string url) | Navigate to URL |
| Reload(bool ignoreCache = false) | Reload page |
| ExecuteJavaScript(string code, ...) | Execute JavaScript |
| EvaluateJavaScript<T>(string code, ...) | Execute JS and return result (C# only) |
| EvalJs(string code) | Async JS eval, result via eval_completed signal (GDScript) |
| ShowDeveloperTools() | Open DevTools |
| CloseDeveloperTools() | Close DevTools |

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

The __hostBridge / __hostEvents naming is engine-agnostic — the same HTML page works with Godot, Unreal, or any CEF host without modification.

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

## CefGlueControl Signals

| Signal | Parameters | Description |
|--------|-----------|-------------|
| BrowserInitialized | — | Browser initialization complete |
| AddressChanged | url: string | Current page URL changed |
| TitleChanged | 	itle: string | Page title changed |
| LoadStart | — | Page starts loading |
| LoadEnd | — | Page finishes loading |
| LoadError | errorText, failedUrl | Page failed to load |
| eval_completed | esult, error | EvalJs result (GDExtension) |
| ridge_request | 	ype, payload, cbId | Bridge request (GDExtension) |

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

1. **Right-click Context Menu**: Not supported
2. **Network Notification**: WSALookupServiceBegin failed with: 10108 is a normal warning
3. **Embedded window focus**: Clicking a Godot input after CEF may not release keyboard focus. Auto-handled via platform-specific APIs.
4. **JS Bridge S prefix**: CefGlue's serialization protocol prepends 'S' marker to strings. Automatically stripped.

## License

GDCefGlue is licensed under the MIT License.

Third-party dependencies:
- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
