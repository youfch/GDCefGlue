# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md)

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
- **Engine-agnostic JS API**: window.__hostBridge / window.__hostEvents — write once, works across Godot, Unreal, or any CEF host

## Quick Start

### C# (Plugin)

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

All CEF files are copied automatically during build. No manual steps required.

> **Note:** For cross-platform builds (Linux/macOS), add the corresponding `cef.redist.*` packages.

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
├── CefGlueControl.Inspector.cs     Inspector property visibility
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
| OpenPopupInCurrentBrowser | bool | false | Feature Toggles | Open popups in current browser |
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
| EvaluateJavaScript<T>(string code, ...) | Execute JS and return result |
| ShowDeveloperTools() | Open DevTools |
| CloseDeveloperTools() | Close DevTools |

## JS ↔ C# Bridge

### RegisterJavascriptObject (Recommended) ✅

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

### JS API Reference

| Object | Method | Description |
|--------|--------|-------------|
| window.__hostBridge | ._onMessage(msg) | Receive push messages from host |
| window.__hostBridge | ._onResponse(cbId, json) | Receive response from host |
| window.__hostEvents | .forward(payload) | Forward input events to host (EmbeddedWindow mode) |

### API Overview

| API | Direction | Description |
|-----|-----------|-------------|
| RegisterJavascriptObject(target, name) | C# → JS | Register object callable from JS |
| EvaluateJavaScript<T>(code, ...) | C# → JS | Execute JS and return result |
| SendToJs(json) | C# → JS | Push message to JS |
| SendResponse(cbId, json) | C# → JS | Reply to bridge request |
| BridgeRequest event | JS → C# | Bridge request entry |

## CefGlueControl Signals

| Signal | Parameters | Description |
|--------|-----------|-------------|
| `BrowserInitialized` | — | Browser initialization complete |
| `AddressChanged` | `url: string` | URL changed |
| `TitleChanged` | `title: string` | Title changed |
| `LoadStart` | — | Page starts loading |
| `LoadEnd` | — | Page finishes loading |
| `LoadError` | `errorText, failedUrl` | Page failed to load |
| `NewWindowRequested` | `url, isNewWindow` | New window/tab requested |

> **Note:** Plugin (C#) uses standard C# events with **PascalCase** naming (e.g. `LoadStart`, `LoadEnd`).  
> GDExtension (GDScript) registers Godot signals with **snake_case** naming (e.g. `load_start`, `load_end`, `eval_completed`, `bridge_request`).  

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
2. Try disabling GPU acceleration

### Missing DLLs
1. Run dotnet restore
2. Clean and rebuild

### Blank Page
1. Check if locales directory exists
2. Ensure esources.pak is present

## Known Issues

1. **Right-click Context Menu**: Not supported
2. **Network Notification**: WSALookupServiceBegin failed with: 10108 is a normal warning
3. **Embedded window focus**: ✅ Fixed. Added `WS_EX_NOACTIVATE` to prevent CEF child window from stealing focus, and bidirectional `OnTakeFocus`/`SetFocus` synchronization via `CefFocusHandler`.
4. **JS Bridge S prefix**: CefGlue's serialization prepends 'S' marker to strings. Automatically stripped.

## License

GDCefGlue is licensed under the MIT License.

Third-party dependencies:
- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
