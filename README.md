# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md)

---

## Features

- **Dual render mode**: OSR (transparent) and EmbeddedWindow (native HWND, high performance)
- **IME support**: Auto IME activation via JS focus watcher (CJK input)
- **Right-click context menu**: Godot PopupMenu in OSR mode, customizable
- **In-page search**: `Find()` / `StopFinding()` + `FindResult` event
- **JS ↔ C#/GDScript bridge**: V8 IPC (no iframe), bidirectional
- **Engine-agnostic JS API**: `window.__hostBridge` / `window.__hostEvents` / `window.__hostFocus`
- **Cross-platform**: Windows (Win32), Linux (X11), macOS (Cocoa)
- **GPU acceleration**, popup handling, IME, keyboard/mouse

---

## Quick Start

### C# (Plugin)

```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
browser.Mode = RenderMode.OSR;   // OSR or EmbeddedWindow
browser.Transparent = true;      // OSR only
AddChild(browser);
```

### GDScript (GDExtension)

```gdscript
var browser = CefGlueControl.new()
browser.InitialUrl = "https://godotengine.org"
browser.Mode = 0  # 0=OSR, 1=EmbeddedWindow
browser.browser_initialized.connect(_on_ready)
add_child(browser)
```

> Full demos: `plugin/demo/` (C#) | `test/GDExtensionGame/demo/` (GDScript)

---

## Requirements

- Godot 4.6+ (with .NET/Mono support)
- .NET SDK 8.0+
- Windows/Linux/macOS (x64 / ARM64)

---

## Installation

### Option A: Plugin (C#)

1. Download the [latest release](https://github.com/youfch/GDCefGlue/releases)
2. Extract `addons/GCefGlue/` to your project's `addons/`
3. Add NuGet packages — see [NuGet Setup](#nuget-setup)

### Option B: GDExtension (GDScript)

1. Download the GDExtension archive from [GitHub Releases](https://github.com/youfch/GDCefGlue/releases)
2. Extract `addons/gdcefglue/` to your project's `addons/`
3. Done — `.gdextension` is auto-configured

### NuGet Setup (Plugin only)

CefGlue packages are published on GitHub Releases, not NuGet.org.

**Download:**
- [CefGlue NuGet packages](https://github.com/youfch/CefGlue/releases/tag/v149.7827.156)
- [chromiumembeddedframework.runtime](https://github.com/youfch/cef.redist.win/releases)

Create `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="local-cefglue" value="./nuget-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

Add to `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="CefGlue.BrowserProcess.runtime.jit" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

---

## Build

### Plugin

```bash
dotnet build plugin/GDCefGlue.csproj
dotnet publish plugin/GDCefGlue.csproj -c Release
```

### Extension (NativeAOT)

```bash
dotnet build extension/GDCefGlueExtension.csproj     # compile check only
dotnet publish extension/GDCefGlueExtension.csproj -c Release -r win-x64
```

AOT output: `extension/bin/Release/net10.0/win-x64/native/GDCefGlueExtension.dll`

> Supported RIDs: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`

---

## Documentation

| Guide | Description |
|-------|-------------|
| [User Guide](doc/USER_GUIDE.md) | Full usage, bridge API, events, troubleshooting |
| [用户指南](doc/USER_GUIDE_CN.md) | 完整使用说明、桥接 API、事件、故障排除 |
| [Bridge TODO](doc/BRIDGE_TODO.md) | Bridge implementation status and plans |

---

## Known Issues

1. `WSALookupServiceBegin failed with: 10108` — normal Windows warning, ignore
2. CefGlue serialization prepends 'S' marker to strings — auto-stripped

---

## License

MIT. Third-party: [CefGlue](https://github.com/youfch/CefGlue) (BSD-3), [CEF](https://bitbucket.org/chromiumembedded/cef) (BSD-3), [Godot](https://godotengine.org) (MIT)
