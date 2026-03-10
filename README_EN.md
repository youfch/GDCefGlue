# GDCefGlue

A CEF (Chromium Embedded Framework) browser control for Godot 4.x using CefGlue.

[中文文档](README_CN.md)

## Features

- GPU hardware acceleration support
- IME support for Chinese/Japanese/Korean input
- Popup handling
- Complete keyboard and mouse support
- Easy integration with Godot 4.x

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
   └── CefGlue/          # Cloned repository
   ```

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

## CefGlueControl Methods

| Method | Description |
|--------|-------------|
| `GoBack()` | Navigate back in history |
| `GoForward()` | Navigate forward in history |
| `NavigateToUrl(string url)` | Navigate to the specified URL |
| `Refresh()` | Reload the current page |

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

- [CefGlue](https://github.com/youfch/CefGlue) - BSD-3-Clause
- [CEF](https://bitbucket.org/chromiumembedded/cef) - BSD-3-Clause
- [Godot Engine](https://godotengine.org) - MIT
