# Multi-Platform Packaging for GDCefGlue GDExtension

## Objective

Package the GDCefGlue GDExtension (`extension/`) as a single addon bundle containing native binaries for **Windows, Linux, and macOS** — modeled after [gdcef](https://github.com/Lecrapouille/gdcef)'s approach (C++ GDExtension). Each platform ships its own AOT-compiled GDExtension library, CEF runtime files, and a NativeAOT BrowserProcess subprocess, all under one `addons/gdcefglue/` tree.

## Reference: gdcef Package Layout

```
cef_artifacts/
├── gdcef.gdextension          # [libraries] + [dependencies] per platform
├── windows/
│   ├── libgdcef.dll           # Native GDExtension library
│   ├── libcef.dll, *.pak      # CEF runtime files
│   ├── locales/
│   └── gdCefRenderProcess.exe # CEF subprocess (native C++)
├── linux/
│   ├── libgdcef.so, libcef.so, *.pak, locales/
│   └── gdCefRenderProcess     # Native ELF
└── macos/
    ├── libgdcef.dylib, locales/
    └── gdCefRenderProcess.app/
```

## Target Layout

```
addons/gdcefglue/
├── gdcefglue.gdextension
├── windows/
│   ├── GDCefGlueExtension.dll               # AOT GDExtension
│   ├── libcef.dll, chrome_*.pak, resources.pak, icudtl.dat, ...
│   ├── locales/
│   └── CefGlueBrowserProcess/
│       └── Xilium.CefGlue.BrowserProcess.exe # NativeAOT
├── linux/
│   ├── libGDCefGlueExtension.so
│   ├── libcef.so, *.pak, locales/
│   └── CefGlueBrowserProcess/
│       └── Xilium.CefGlue.BrowserProcess     # NativeAOT ELF
└── macos/
    ├── libGDCefGlueExtension.dylib
    ├── libcef.dylib, *.pak, locales/
    └── CefGlueBrowserProcess/
        └── Xilium.CefGlue.BrowserProcess     # NativeAOT Mach-O
```

## .gdextension File

```ini
[configuration]
entry_symbol = "gdcefglue_library_init"
compatibility_minimum = 4.6

[libraries]
windows.x86_64.debug = "res://addons/gdcefglue/windows/GDCefGlueExtension.dll"
windows.x86_64.release = "res://addons/gdcefglue/windows/GDCefGlueExtension.dll"
linux.x86_64.debug = "res://addons/gdcefglue/linux/libGDCefGlueExtension.so"
linux.x86_64.release = "res://addons/gdcefglue/linux/libGDCefGlueExtension.so"
macos.debug = "res://addons/gdcefglue/macos/libGDCefGlueExtension.dylib"
macos.release = "res://addons/gdcefglue/macos/libGDCefGlueExtension.dylib"

[dependencies]
windows.x86_64 = {
  "res://addons/gdcefglue/windows/libcef.dll": "",
  "res://addons/gdcefglue/windows/resources.pak": "",
  "res://addons/gdcefglue/windows/chrome_100_percent.pak": "",
  "res://addons/gdcefglue/windows/chrome_200_percent.pak": "",
  "res://addons/gdcefglue/windows/icudtl.dat": "",
  "res://addons/gdcefglue/windows/v8_context_snapshot.bin": "",
  "res://addons/gdcefglue/windows/vk_swiftshader.dll": "",
  "res://addons/gdcefglue/windows/vk_swiftshader_icd.json": "",
  "res://addons/gdcefglue/windows/vulkan-1.dll": "",
  "res://addons/gdcefglue/windows/chrome_elf.dll": "",
  "res://addons/gdcefglue/windows/d3dcompiler_47.dll": "",
  "res://addons/gdcefglue/windows/libEGL.dll": "",
  "res://addons/gdcefglue/windows/libGLESv2.dll": ""
}
linux.x86_64 = {
  "res://addons/gdcefglue/linux/libcef.so": "",
  "res://addons/gdcefglue/linux/resources.pak": "",
  "res://addons/gdcefglue/linux/chrome_100_percent.pak": "",
  "res://addons/gdcefglue/linux/chrome_200_percent.pak": "",
  "res://addons/gdcefglue/linux/icudtl.dat": "",
  "res://addons/gdcefglue/linux/v8_context_snapshot.bin": "",
  "res://addons/gdcefglue/linux/libEGL.so": "",
  "res://addons/gdcefglue/linux/libGLESv2.so": "",
  "res://addons/gdcefglue/linux/libvk_swiftshader.so": "",
  "res://addons/gdcefglue/linux/libvulkan.so.1": "",
  "res://addons/gdcefglue/linux/vk_swiftshader_icd.json": ""
}
```

## Implementation Steps

### 1. Verify NuGet Packages

- [ ] `CefGlue.BrowserProcess.runtime.aot` version `149.7827.156` — if it exists, use directly. Otherwise, build BrowserProcess manually with NativeAOT (Step 1b).
- [ ] `chromiumembeddedframework.runtime.linux-x64` / `.osx-x64` version `149.0.4` — if absent, use `cef.redist.linux64` / `cef.redist.osx64` (OutSystems) instead.

### 1b. (Fallback) Build BrowserProcess with NativeAOT

```bash
git clone https://github.com/youfch/CefGlue.git
dotnet publish CefGlue/CefGlue.BrowserProcess -c Release -r win-x64  -p:PublishAot=true -o BrowserProcess/win-x64/
dotnet publish CefGlue/CefGlue.BrowserProcess -c Release -r linux-x64 -p:PublishAot=true -o BrowserProcess/linux-x64/
dotnet publish CefGlue/CefGlue.BrowserProcess -c Release -r osx-x64  -p:PublishAot=true -o BrowserProcess/osx-x64/
```

Each produces a single native executable with no .NET runtime dependency.

### 2. Simplify CefInitializer.cs

Replace the runtime path-resolution sprawl with a deterministic structure:

- Auto-detect platform (`windows`/`linux`/`macos`).
- Search paths (in order):
  1. `res://addons/gdcefglue/{platform}/` (editor/dev)
  2. `AppContext.BaseDirectory` (exported game)
  3. Fallback to current `GetExtensionDirectory()`
- Look for `resources.pak` + `locales/` under the platform directory.
- Look for BrowserProcess under `{platform}/CefGlueBrowserProcess/`.
- Remove `PreloadCefDependencies` (NuGet handles DLL loading now).
- Simplify `FindCefLibraryPath` to `{platform}/libcef.*`.

### 3. Build Script / CI Pipeline

A Python script or GitHub Actions matrix that:

1. `dotnet restore` for each RID (`win-x64`, `linux-x64`, `osx-x64`)
2. `dotnet publish -p:PublishAot=true` for each RID
3. Collect CEF files from NuGet cache per platform
4. Collect BrowserProcess (from Step 1 or 1b)
5. Assemble into `addons/gdcefglue/{platform}/`
6. Generate `gdcefglue.gdextension`

### 4. Update csproj

- Remove `CopyCefFiles` targets (already done).
- Add `CefGlue.BrowserProcess.runtime.aot` (if available) or keep as-is.
- Add platform CEF runtime packages for CI consumption:
  ```xml
  <PackageReference Include="chromiumembeddedframework.runtime.win-x64" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.linux-x64" Version="149.0.4" />
  <PackageReference Include="chromiumembeddedframework.runtime.osx-x64" Version="149.0.4" />
  ```

### 5. Test

- [ ] Windows: navigation, JS bridge, OSR, EmbeddedWindow
- [ ] Linux: same
- [ ] macOS: same
- [ ] Godot export: platform-specific filtering works

## Open Questions

1. **Does `CefGlue.BrowserProcess.runtime.aot` exist?** If not, can `CefGlue.BrowserProcess` be built with `PublishAot=true`?
2. **Does `chromiumembeddedframework.runtime` have linux-x64 / osx-x64 variants?** If not, fall back to `cef.redist.*`.
3. **Runtime path**: Can the GDExtension reliably locate its own `.gdextension` directory at runtime? Current `GetExtensionDirectory()` approximates this but may not cover all export scenarios.
4. **`[dependencies]` export behavior**: Does Godot correctly include the declared dependencies in exported builds? Test with large `.pak` files.
5. **macOS code signing**: NativeAOT binaries need signing. Who handles this?
6. **Package size**: ~150-200MB per platform, ~500MB total. Acceptable?

## Success Criteria

- [ ] Single archive with all 3 platforms.
- [ ] Install by extracting `addons/gdcefglue/` into project → works immediately.
- [ ] No .NET runtime required on target machine.
- [ ] CEF files found automatically at runtime.
- [ ] Godot export filters platform files correctly.
- [ ] All existing features preserved: navigation, JS bridge, EmbeddedWindow, OSR.