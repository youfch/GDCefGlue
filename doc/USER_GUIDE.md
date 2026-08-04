# GDCefGlue User Guide

[中文文档](USER_GUIDE_CN.md)

## Overview

GDCefGlue embeds a full Chromium browser (CEF) into Godot 4.x as a `Control` node. It supports two rendering modes and provides a complete JS ↔ C#/GDScript bridge.

---

## Installation

### Option A: Plugin (C#, Godot.NET.Sdk)

1. **Create a Godot .NET project** (Godot 4.6+).
2. **Download the latest release** from [GitHub Releases](https://github.com/youfch/GDCefGlue/releases).
3. **Extract** `addons/GCefGlue/` into your project's `addons/` directory.
4. **Add NuGet packages** — see [NuGet Setup](#nuget-setup) below.

### Option B: GDExtension (NativeAOT, GDScript)

1. **Download the GDExtension archive** from [GitHub Releases](https://github.com/youfch/GDCefGlue/releases).
2. **Extract** `addons/gdcefglue/` into your project's `addons/` directory.
3. The `.gdextension` file is auto-configured — no additional setup needed.

### NuGet Setup (Plugin only)

CefGlue NuGet packages are published on GitHub Releases (not on NuGet.org). Download and set up a local feed.

**Download:**
- [CefGlue NuGet packages](https://www.nuget.org/packages/CefGlue.Common/120.6099.211) — use `CefGlue.Common` from nuget.org
- [chromiumembeddedframework.runtime](https://github.com/youfch/cef.redist.win/releases) — CEF runtime packages

**Setup:**

1. Download all `.nupkg` files, place them in `./nuget-feed/`
2. Create a `nuget.config` in your project root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-cefglue" value="./nuget-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

Add to your `.csproj`:

```xml
<ItemGroup>
<PackageReference Include="CefGlue.Common" Version="120.6099.211" />
<PackageReference Include="chromiumembeddedframework.runtime" Version="120.1.8" />
<PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="120.1.8" />
</ItemGroup>
```

---

## Basic Usage

### Add a Browser to Your Scene

**C#:**
```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;   // OSR (transparent) or EmbeddedWindow
AddChild(browser);
```

> Full C# demo: `plugin/demo/` directory.

**GDScript:**
```gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow
add_child(browser)
```

> Full GDScript demo: `test/GDExtensionGame/demo/` directory.

### Inspector Setup

In the Godot editor, add a `CefGlueControl` node to your scene. Key Inspector properties:

| Property | Default | Description |
|----------|---------|-------------|
| InitialUrl | `about:blank` | URL to load on startup |
| Mode | `OSR` | `OSR` (transparent, cross-platform) or `EmbeddedWindow` (native HWND, better video/WebGL) |
| FrameRate | `60` | Browser FPS (1-360, OSR only) |
| Transparent | `false` | Enable alpha transparency (OSR only) |
| ContextMenuEnabled | `true` | Show right-click context menu (OSR only) |
| EnableGpuAcceleration | `false` | ⚠️ **Experimental** — not production-ready, keep disabled by default |

---

## Rendering Modes

### OSR (Off-Screen Rendering) — Default

> **Note:** GPU acceleration (`EnableGpuAcceleration`) is experimental and not production-ready. Use with caution.

CEF renders the page into memory → Godot draws it as a texture.

**Pros:**
- ✅ Alpha transparency support
- ✅ Cross-platform (Windows/Linux/macOS)
- ✅ Works in any Godot container (ScrollContainer, etc.)
- ✅ IME input method support

**Cons:**
- ❌ Lower video/WebGL performance
- ❌ Higher CPU usage (software rendering path)

### EmbeddedWindow

> ⚠️ **Linux**: Embedded mode is incomplete on Linux — only windowed mode is supported. OSR is recommended. Full embedded support is planned.

CEF creates a native child OS window embedded in the Godot window.

**Pros:**
- ✅ GPU hardware acceleration
- ✅ Smooth video/WebGL playback
- ✅ Lower CPU usage

**Cons:**
- ❌ No transparency
- ❌ OS-specific behavior (focus, z-order)
- ❌ May not work inside all Godot containers

---

## Connections & Events

### C# Events

```csharp
browser.BrowserInitialized += () => GD.Print("Browser ready");
browser.LoadEnd += (sender, args) => GD.Print("Page loaded");
browser.AddressChanged += (sender, url) => GD.Print("URL: " + url);
browser.TitleChanged += (sender, title) => GD.Print("Title: " + title);
browser.LoadError += (sender, args) => GD.PrintErr("Load error: " + args.ErrorText);
```

### GDScript Signals

```gdscript
browser.browser_initialized.connect(_on_ready)
browser.load_end.connect(_on_done)
browser.address_changed.connect(_on_address_changed)

func _on_ready():
    print("Browser ready")
    browser.eval_js("console.log('Hello from Godot!')")
```

---

## JS ↔ C# Bridge

### Register C# Object (Plugin)

Register any C# object so JavaScript can call its methods:

```csharp
public class MyBridge
{
    public string Hello(string name) => $"Hello, {name}!";
    public int Add(int a, int b) => a + b;
}

// Register
browser.RegisterJavascriptObject(new MyBridge(), "myBridge");
```

**JavaScript call:**
```javascript
window.myBridge.hello("World").then(r => console.log(r)); // "Hello, World!"
window.myBridge.add(2, 3).then(r => console.log(r));      // 5
```

### Register GDScript Handler (GDExtension)

```gdscript
browser.register_js_handler("dotnetBridge", Callable(self, "_on_js_call"))

func _on_js_call(method_name: String, args_json: String) -> Variant:
    match method_name:
        "hello":
            return "Hello from GDScript!"
        "add":
            var arr = JSON.parse_string(args_json) as Array
            return int(arr[0]) + int(arr[1])
```

### Push Messages from Host to JS

```csharp
// C#
browser.SendToJs("{\"type\":\"update\",\"payload\":{\"count\":42}}");
```

```gdscript
# GDScript
browser.send_to_js('{"type":"update","payload":{"count":42}}')
```

**JS receives via:**
```javascript
window.__hostBridge._onMessage = function(msg) {
    console.log("Received from host:", msg);
};
```

### Execute JS and Get Result

```csharp
// C# (async, returns Task<T>)
var title = await browser.EvaluateJavaScript<string>("document.title");
var count = await browser.EvaluateJavaScript<int>("document.querySelectorAll('a').length");
```

```gdscript
# GDScript (async, result via signal)
browser.eval_js("document.title")
# Result received in:
func _on_eval_done(result: String, error: String):
    if error.is_empty():
        print("JS result: ", result)
```

---

## IME / Input Method (Chinese/Japanese/Korean)

IME is automatically handled by the JS focus watcher:

- **Click on an input field** → JS detects `focusin` → automatically activates IME
- **Click outside the input** → JS detects `focusout` → automatically deactivates IME
- **No manual IME management needed**

Works in both OSR and EmbeddedWindow modes.

---

## Context Menu (Right-Click)

In OSR mode, right-clicking shows a Godot `PopupMenu` with CEF default items (Back, Forward, Reload, Copy, Paste, Inspect, etc.).

- **Enable/disable**: Set `ContextMenuEnabled` in the Inspector
- **Customize**: Subscribe to `BeforeContextMenu` event to modify menu items

```csharp
browser.BeforeContextMenu += (model, params) => {
    model.Clear();
    model.AddItem(26500, "Custom Action");  // UserFirst = 26500
};
```

---

## In-Page Search

```csharp
// Start search
browser.Find("search text", forward: true, matchCase: false, findNext: false);

// Stop search
browser.StopFinding(clearSelection: true);

// Handle results
browser.FindResult += (identifier, count, activeMatchOrdinal, finalUpdate) => {
    GD.Print($"Found {count} matches, current: {activeMatchOrdinal}");
};
```

---

## Navigation & DevTools

```csharp
browser.GoBack();                           // Back
browser.GoForward();                        // Forward
browser.NavigateToUrl("https://example.com"); // Navigate
browser.Reload();                           // Reload
browser.ShowDeveloperTools();               // Open DevTools
browser.CloseDeveloperTools();              // Close DevTools
```

---

## Platform-Specific Notes

### Windows

- **EmbeddedWindow**: Uses Win32 child HWND. `WS_EX_NOACTIVATE` prevents focus stealing.
- **CEF files**: All DLLs are auto-copied during build.
- **Locales**: Ensure `locales/` directory exists next to your executable.

### Linux

- **EmbeddedWindow**: ⚠️ Incomplete on Linux. Only windowed mode is supported; embedded mode does not work properly inside containers. **OSR is recommended on Linux for now.** Full embedded support is planned.
- **Dependencies**: Install `libxkbcommon-x11-dev` for keyboard support.
- **AOT build**: Requires `clang` and `zlib1g-dev`.
- **⚠️ Must use the launch scripts**: CEF's `libcef.so` uses the initial-exec TLS model, which causes a SEGV when loaded via `dlopen` on Linux. You must use `scripts/run_gde_linux.sh` or `scripts/run_plugin_linux.sh` to launch your project — these scripts set `LD_PRELOAD` to preload `libcef.so`. Running Godot directly will crash. See [Linux Troubleshooting Guide](TROUBLESHOOTING_LINUX_KYLIN.md) for details.

### macOS

- **EmbeddedWindow**: Uses Cocoa NSView.
- **AOT build**: Requires Xcode Command Line Tools.
- **Notarization**: CEF binaries may need codesigning for distribution.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **Blank page** | Check `locales/` directory and `resources.pak` exist |
| **GPU crash** | `EnableGpuAcceleration` is experimental and not production-ready. Keep it disabled by default. |
| **Missing DLLs** | Run `dotnet restore` and rebuild |
| **IME not working** | Ensure `__hostFocus` V8 object is registered (check debug output) |
| **Right-click does nothing** | Set `ContextMenuEnabled = true` in Inspector |
| **WSALookupServiceBegin error** | Normal Windows warning, ignore |

---

## Project Structure

```
addons/GCefGlue/          ← Plugin (C#)
├── CefGlueControl.cs     ← Browser control node
├── CefInitializer.cs     ← CEF startup
└── Handlers/             ← CEF event handlers

addons/gdcefglue/         ← GDExtension (NativeAOT)
├── gdcefglue.gdextension ← Extension config
├── windows-x64/          ← Windows binaries
├── linux-x64/            ← Linux binaries
└── macos-arm64/          ← macOS binaries
```

---

## Build from Source

See [Build section](README.md#build) in the main README.