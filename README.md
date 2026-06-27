# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md)

## Features

- GPU hardware acceleration support
- IME support for Chinese/Japanese/Korean input
- Popup handling
- Complete keyboard and mouse support
- Easy integration with Godot 4.x

## Performance Demo

### WebGL Aquarium

Supports WebGL rendering with 20,000 fish at stable 120fps:

![WebGL Aquarium](img/WebGL水族馆.png)

## Quick Start

### C# (Plugin)

Add a `CefGlueControl` node to your scene:

```csharp
var browser = new CefGlueControl();
browser.InitialUrl = "https://godotengine.org";
AddChild(browser);
```

### GDScript (GDExtension)

```gdscript
var browser: CefGlueControl = $CefGlueControl
browser.InitialUrl = "https://godotengine.org"
browser.FrameRate = 120

# Connect to signals
browser.BrowserInitialized.connect(_on_ready)
browser.AddressChanged.connect(_on_address_changed)
browser.LoadStart.connect(_on_loading)
browser.LoadEnd.connect(_on_done)
browser.LoadError.connect(_on_error)
```

## Requirements

- **Godot Engine**: 4.6.0 or later (with .NET/Mono support)
- **.NET SDK**: 8.0 or later
- **Windows**: x64 architecture

## Dependency Options

### Option 1: NuGet Package (Recommended)

The easiest way to use GDCefGlue. All necessary files are automatically copied during build.

**CEF 149 (Recommended):**
```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="149.7827.156" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
</ItemGroup>
```

**CEF 120 (Official, not recommended — too old):**
```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="120.6099.0" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="120.2.3" />
</ItemGroup>
```

### Option 2: Build from Source

If you need the latest version or want to customize CefGlue:

```bash
git clone https://github.com/youfch/CefGlue.git
```

Place the cloned repository in your project directory:
```
YourProject/
├── GDCefGlue/                    ← This repository
│   ├── plugin/                   ← Godot .NET project (addon source + demo)
│   │   └── addons/GCefGlue/     ←    CefGlueControl C# scripts
│   ├── extension/                ← GDExtension C# project (AOT native lib)
│   │   └── Dll/                  ←    godot-dotnet bindings
│   ├── test/GDExtensionGame/     ← Godot project for testing GDExtension
│   ├── Nuget/                    ← Local NuGet packages (LFS)
│   ├── img/
│   └── README*.md
└── CefGlue/                      ← Cloned CefGlue repository
```

## Project Types

### GDExtension Project (Experimental)

Located in `extension/` directory. For Godot 4.6+.

> **Warning:** This project is currently unstable and not recommended for production use.

**Features:**
- Uses Godot's GDExtension system
- Compiled as native AOT library
- Better performance and smaller file size
- Requires manual CEF file copying during export
- Entry point: `gdcefglue_library_init`

**Structure:**
- `extension/` - C# source code for GDExtension
- `extension/Dll/` - Godot .NET bindings (from [godot-dotnet](https://github.com/raulsntos/godot-dotnet))
- `test/GDExtensionGame/` - Godot project for testing

**Build Instructions:**

1. **Get Godot .NET Bindings:**
   ```bash
   git clone https://github.com/raulsntos/godot-dotnet.git
   cd godot-dotnet
   dotnet build -p:GenerateGodotBindings=true
   ```
   Copy the generated `Godot.Bindings.dll` and related files to `extension/Dll/`.

2. **Build GDExtension:**
   ```bash
   cd extension
   dotnet publish -c Debug -r win-x64 --self-contained true
   ```

3. **Deploy:**
   Copy all files from `Debug/net10.0/win-x64/publish/` to `test/GDExtensionGame/lib/`.

4. **Run:**
   Open the project with Godot 4.6 and run.

### Normal C# Project

Located in `plugin/` directory. Traditional Godot C# project approach.

**Features:**
- Standard Godot .NET SDK project
- Uses `Godot.NET.Sdk`
- CEF files are automatically copied when using NuGet packages
- May need manual resource copying after export (except NuGet packages)
- Source code in `addons/GCefGlue/`

**When to use:**
- If you prefer traditional Godot C# development
- If GDExtension doesn't meet your needs

Update your `.csproj` to reference the projects:
```xml
<ItemGroup>
  <ProjectReference Include="..\CefGlue\CefGlue\CefGlue.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common\CefGlue.Common.csproj" />
  <ProjectReference Include="..\CefGlue\CefGlue.Common.Shared\CefGlue.Common.Shared.csproj" />
</ItemGroup>
```

### CefGlue Sources

| Source | CEF Version | Status | NuGet | GitHub |
|--------|-------------|--------|-------|--------|
| youfch/CefGlue | 149 | Maintained (Recommended) | `CefGlue.Common 149.7827.156` | [GitHub](https://github.com/youfch/CefGlue) |
| youfch/cef.redist.linux | 149 | Linux runtime | `cef.redist.linux64 149.0.4` | [GitHub](https://github.com/youfch/cef.redist.linux) |
| youfch/cef.redist.osx | 149 | macOS runtime | `cef.redist.osx64 149.0.4` | [GitHub](https://github.com/youfch/cef.redist.osx) |
| OutSystems/CefGlue | 120 | Official (Legacy) | `CefGlue.Common 120.6099.0` | [GitHub](https://github.com/OutSystems/CefGlue) |

## Build Instructions

### Using NuGet Package

```powershell
# Restore packages
dotnet restore

# Build
dotnet build
```

### Using Source Code

```powershell
# Clone CefGlue
git clone https://github.com/youfch/CefGlue.git

# Build
dotnet restore
dotnet build
```

## Build Output

After a successful build, the following files will be generated:

**Core Files:**
- `GDCefGlue.dll` - Main plugin assembly
- `Xilium.CefGlue.dll` - CefGlue wrapper
- `Xilium.CefGlue.Common.dll` - Common functionality

**CEF Native Files:**
- `libcef.dll` - Chromium core library
- `chrome_*.pak` - UI resources
- `resources.pak` - Application resources
- `locales\*.pak` - Language packs

**BrowserProcess Files:**
- `CefGlueBrowserProcess\` - Browser subprocess files

## Export for Distribution

### Using NuGet Package

When using NuGet packages, all necessary files are automatically copied during build. Just export your Godot project normally.

### Using Source Code

When using source code dependencies, you need to manually copy files after export. See [CEF_EXPORT_GUIDE.md](CEF_EXPORT_GUIDE.md) for details.

## CefGlueControl Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InitialUrl` | string | "about:blank" | The URL to load when the browser is created |
| `OpenPopupInCurrentBrowser` | bool | true | If true, popup windows navigate in the current browser |
| `GpuAcceleration` | bool | true | If true, enables GPU hardware acceleration |
| `FrameRate` | int | 60 | Browser frame rate, range 1-360 |
| `Transparent` | bool | false | If true, enables transparent background support |

### Static Properties

| Property | Type | Description |
|----------|------|-------------|
| `UseGpuAcceleration` | bool | Global GPU acceleration setting, must be set before CEF initialization |
| `UseTransparent` | bool | Global transparent background setting, must be set before CEF initialization |

### Read-only Properties

| Property | Type | Description |
|----------|------|-------------|
| `Address` | string | Current page URL |
| `IsBrowserInitialized` | bool | Whether the browser is initialized |
| `IsLoading` | bool | Whether the page is loading |
| `Title` | string | Current page title |

## CefGlueControl Methods

| Method | Description |
|--------|-------------|
| `GoBack()` | Navigate back in history |
| `GoForward()` | Navigate forward in history |
| `NavigateToUrl(string url)` | Navigate to the specified URL |
| `Reload(bool ignoreCache = false)` | Reload the current page, optionally ignoring cache |
| `ExecuteJavaScript(string code, ...)` | Execute JavaScript code |
| `EvaluateJavaScript<T>(string code, ...)` | Execute JavaScript and return result |
| `ShowDeveloperTools()` | Open developer tools |
| `CloseDeveloperTools()` | Close developer tools |

## CefGlueControl Signals

| Signal | Parameters | Description |
|--------|-----------|-------------|
| `BrowserInitialized` | — | Emitted when the browser is fully initialized and ready |
| `AddressChanged` | `url: string` | Emitted when the current page URL changes |
| `TitleChanged` | `title: string` | Emitted when the page title changes |
| `LoadStart` | — | Emitted when a page starts loading |
| `LoadEnd` | — | Emitted when a page finishes loading |
| `LoadError` | `errorText: string, failedUrl: string` | Emitted when a page fails to load |

## GPU Configuration

```csharp
// Enable GPU acceleration (default) - set in inspector
// GpuAcceleration = true;

// Disable GPU acceleration (software rendering) - set in inspector
// GpuAcceleration = false;
```

**Note:** This property is exposed to the Godot inspector. Set it before the control is initialized.

## Troubleshooting

### GPU Process Crashed

1. Ensure all CEF files are copied correctly
2. Try disabling GPU acceleration in the inspector

### Missing DLLs

1. Run `dotnet restore` to restore NuGet packages
2. Clean and rebuild the solution

### Blank Page

1. Check if `locales` directory exists
2. Ensure `resources.pak` is present

## Known Issues

1. **Right-click Context Menu**: Not supported
2. **Network Notification**: `WSALookupServiceBegin failed with: 10108` is a normal warning

## License

GDCefGlue is licensed under the MIT License. See [LICENSE](LICENSE) for details.

Third-party dependencies:
- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
- [godot-dotnet](https://github.com/raulsntos/godot-dotnet) - MIT
