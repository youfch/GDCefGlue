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

## Requirements

- **Godot Engine**: 4.6.0 or later (with .NET/Mono support)
- **.NET SDK**: 8.0 or later
- **Windows**: x64 architecture

## Dependency Options

### Option 1: NuGet Package (Recommended)

The easiest way to use GDCefGlue. All necessary files are automatically copied during build.

```xml
<ItemGroup>
  <PackageReference Include="CefGlue.Common" Version="134.6998.178" />
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="134.3.9" />
</ItemGroup>
```

**Note:** The official NuGet package only has CEF 120 version. For CEF 134 version, use the unofficial package from [youfch/CefGlue](https://github.com/youfch/CefGlue).

### Option 2: Build from Source

If you need the latest version or want to customize CefGlue:

1. Clone the repository:
   ```bash
   git clone https://github.com/youfch/CefGlue.git
   ```

2. Place it in your project directory:
   ```
   YourProject/
   ├── GDCefGlue/
   │   ├── GDExtension/
   │   │   ├── Extension/     # C# GDExtension source code
   │   │   └── Project/       # Godot test project
   │   ├── NormalProject/     # Normal C# project example
   │   ├── img/
   │   └── README*.md
   └── CefGlue/               # Cloned repository
   ```

## Project Types

### GDExtension Project

Located in `GDExtension/` directory. For Godot 4.6+.

**Features:**
- Uses Godot's GDExtension system
- Compiled as native AOT library
- Better performance and smaller file size
- Requires manual CEF file copying during export
- Entry point: `gdcefglue_library_init`

**Structure:**
- `GDExtension/Extension/` - C# source code for GDExtension
- `GDExtension/Extension/Dll/` - Godot .NET bindings (from [godot-dotnet](https://github.com/raulsntos/godot-dotnet))
- `GDExtension/Project/` - Godot project for testing

**Build Instructions:**

1. **Get Godot .NET Bindings:**
   
   Different Godot versions require corresponding godot-dotnet versions:
   
   | Godot Version | godot-dotnet Branch |
   |---------------|---------------------|
   | 4.6.x         | master or corresponding tag |
   | 4.5.x         | Check corresponding release tag |
   | 4.4.x         | Check corresponding release tag |
   
   > **Note:** godot-dotnet does not publish release packages. You need to check the commit history, download the source code for your Godot version, and compile manually.
   
   ```bash
   git clone https://github.com/raulsntos/godot-dotnet.git
   cd godot-dotnet
   # Checkout the branch/tag for your Godot version (if needed)
   # git checkout <godot-version-tag>
   dotnet build -p:GenerateGodotBindings=true
   ```
   Copy the generated `Godot.Bindings.dll` and related files to `GDExtension/Extension/Dll/`.

2. **CEF Dependencies (Cross-Platform):**
   
   - **Windows:** Automatically obtained via NuGet package `chromiumembeddedframework.runtime.win-x64`
   - **Linux:** Requires [cef.redist.linux](https://github.com/OutSystems/cef.redist.linux) dependency
   - **macOS:** Requires [cef.redist.osx](https://github.com/OutSystems/cef.redist.osx) dependency
   
   See [CefGlue repository](https://github.com/youfch/CefGlue) for how to add cross-platform dependencies.

3. **Build GDExtension:**
   
   Navigate to `GDExtension/Extension` directory and run:
   
   **Windows x64:**
   ```bash
   # Debug build
   dotnet publish -c Debug -r win-x64 --self-contained true
   
   # Release build
   dotnet publish -c Release -r win-x64 --self-contained true
   ```
   
   **Linux x64:**
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true
   ```
   
   **macOS x64/ARM64:**
   ```bash
   # Intel Mac
   dotnet publish -c Release -r osx-x64 --self-contained true
   
   # Apple Silicon Mac
   dotnet publish -c Release -r osx-arm64 --self-contained true
   ```

4. **Deploy:**
   
   Build output is located at `bin\Release(Debug)\net10.0\win-x64\publish\` (Windows)
   
   Copy all files from the publish directory to `GDExtension/Game/lib/`.

5. **Run:**
   Open `GDExtension/Game/` project with Godot 4.6 and run.

**Different CEF Version Support:**

To use different CEF versions, there are two options:

1. **NuGet Package (Recommended):** Modify the NuGet package version in `.csproj`
   ```xml
   <PackageReference Include="CefGlue.Common" Version="xxx.xxxx.x" />
   <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="xxx.x.x" />
   ```

2. **Manual Build:** Download the source code for the corresponding CEF version from [CefGlue repository](https://github.com/youfch/CefGlue), manually compile the NuGet package, and reference it.

### Normal C# Project

Located in `NormalProject/` directory. Traditional Godot C# project approach.

**Features:**
- Standard Godot .NET SDK project
- Uses `Godot.NET.Sdk`
- CEF files are automatically copied when using NuGet packages
- May need manual resource copying after export (except NuGet packages)
- Source code in `addons/GCefGlue/`

**When to use:**
- If you prefer traditional Godot C# development
- If GDExtension doesn't meet your needs

3. Update your `.csproj` to reference the projects:
   ```xml
   <ItemGroup>
     <ProjectReference Include="..\CefGlue\CefGlue\CefGlue.csproj" />
     <ProjectReference Include="..\CefGlue\CefGlue.Common\CefGlue.Common.csproj" />
     <ProjectReference Include="..\CefGlue\CefGlue.Common.Shared\CefGlue.Common.Shared.csproj" />
   </ItemGroup>
   ```

### CefGlue Sources

| Source | CEF Version | Status | Link |
|--------|-------------|--------|------|
| youfch/CefGlue | 134 | Unofficial (Recommended) | [GitHub](https://github.com/youfch/CefGlue) |
| OutSystems/CefGlue | 120 | Official | [GitHub](https://github.com/OutSystems/CefGlue) |

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
2. Try disabling GPU acceleration: `CefGlueControl.UseGpuAcceleration = false;`

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
