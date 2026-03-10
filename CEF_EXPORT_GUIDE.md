# CEF Export File Copy Guide (Source Code Dependencies)

This guide is only for **source code dependencies**. If you are using NuGet packages, all necessary files are automatically copied during build and you don't need this guide.

## Overview

When using source code dependencies (cloned CefGlue repository), Godot's export process does NOT automatically copy CEF-related files. You must manually copy files after export.

## Export Directory

Default export directory: `<ExportDir>\data_GDCefGlue_windows_x86_64\`

---

## Files to Copy

### 1. BrowserProcess Files (Required!)

**Source Directory:**
```
CefGlue\CefGlue.BrowserProcess\bin\Debug\net8.0\win-x64\publish\
```

**Target:** `<ExportDir>\data_GDCefGlue_windows_x86_64\`

**Files:**
- `Xilium.CefGlue.BrowserProcess.exe`
- `Xilium.CefGlue.BrowserProcess.dll`
- `Xilium.CefGlue.BrowserProcess.runtimeconfig.json`
- `Xilium.CefGlue.BrowserProcess.deps.json`
- `Xilium.CefGlue.dll`
- `Xilium.CefGlue.Common.Shared.dll`

---

### 2. CEF Native Files

**Source Directory:**
```
CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\runtimes\win-x64\native\
```

**Target:** Export directory root

**Files:**
- `chrome_100_percent.pak`
- `chrome_200_percent.pak`
- `chrome_elf.dll`
- `d3dcompiler_47.dll`
- `dxcompiler.dll`
- `dxil.dll`
- `icudtl.dat`
- `libEGL.dll`
- `libGLESv2.dll`
- `libcef.dll`
- `resources.pak`
- `v8_context_snapshot.bin`
- `vk_swiftshader.dll`
- `vk_swiftshader_icd.json`
- `vulkan-1.dll`

---

### 3. Locales Files

**Source Directory:**
```
CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\CEF\win-x64\locales\
```

**Target:** `Export directory\locales\`

**Files:** All `.pak` files

---

## Copy Commands

### PowerShell Commands

```powershell
# Set paths (using relative paths)
$projectRoot = "."  # Current directory, or specify project root
$exportDir = "<ExportDir>\data_GDCefGlue_windows_x86_64"  # Export data directory path
$browserProcessSrc = "$projectRoot\CefGlue\CefGlue.BrowserProcess\bin\Debug\net8.0\win-x64\publish"
$cefSrc = "$projectRoot\CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\runtimes\win-x64\native"
$localesSrc = "$projectRoot\CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\CEF\win-x64\locales"

# 1. Copy BrowserProcess files
Copy-Item "$browserProcessSrc\Xilium.CefGlue.BrowserProcess.exe" $exportDir -Force
Copy-Item "$browserProcessSrc\Xilium.CefGlue.BrowserProcess.dll" $exportDir -Force
Copy-Item "$browserProcessSrc\Xilium.CefGlue.BrowserProcess.runtimeconfig.json" $exportDir -Force
Copy-Item "$browserProcessSrc\Xilium.CefGlue.BrowserProcess.deps.json" $exportDir -Force
Copy-Item "$browserProcessSrc\Xilium.CefGlue.dll" $exportDir -Force
Copy-Item "$browserProcessSrc\Xilium.CefGlue.Common.Shared.dll" $exportDir -Force

# 2. Copy CEF native files
Copy-Item "$cefSrc\*.*" $exportDir -Force

# 3. Copy locales
$localesDest = "$exportDir\locales"
if (!(Test-Path $localesDest)) { New-Item -ItemType Directory -Path $localesDest -Force }
Copy-Item "$localesSrc\*.pak" $localesDest -Force

Write-Host "Copy complete!"
```

### CMD Commands

```batch
@echo off
set PROJECT_ROOT=.
set EXPORT_DIR=<ExportDir>\data_GDCefGlue_windows_x86_64
set BROWSER_PROCESS=%PROJECT_ROOT%\CefGlue\CefGlue.BrowserProcess\bin\Debug\net8.0\win-x64\publish
set CEF_SRC=%PROJECT_ROOT%\CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\runtimes\win-x64\native
set LOCALES_SRC=%PROJECT_ROOT%\CefGlue\packages\chromiumembeddedframework.runtime.win-x64\<version>\CEF\win-x64\locales

echo Copying BrowserProcess files...
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.BrowserProcess.exe" "%EXPORT_DIR%\"
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.BrowserProcess.dll" "%EXPORT_DIR%\"
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.BrowserProcess.runtimeconfig.json" "%EXPORT_DIR%\"
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.BrowserProcess.deps.json" "%EXPORT_DIR%\"
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.dll" "%EXPORT_DIR%\"
copy /Y "%BROWSER_PROCESS%\Xilium.CefGlue.Common.Shared.dll" "%EXPORT_DIR%\"

echo Copying CEF native files...
copy /Y "%CEF_SRC%\*.*" "%EXPORT_DIR%\"

echo Copying locales...
if not exist "%EXPORT_DIR%\locales" mkdir "%EXPORT_DIR%\locales"
copy /Y "%LOCALES_SRC%\*.pak" "%EXPORT_DIR%\locales\"

echo Copy complete!
```

**Note:** Replace `<version>` with the actual CEF version number (e.g., `134.3.9`).

---

## Export Directory Structure

```
<ExportDir>\
├── Game.exe                           # Godot main executable
└── data_GDCefGlue_windows_x86_64\     # Data directory
    ├── locales\                       # CEF language packs
    │   ├── zh-CN.pak
    │   └── ...
    ├── CefGlueBrowserProcess\         # Browser subprocess (self-contained)
    │   ├── Xilium.CefGlue.BrowserProcess.exe
    │   ├── Xilium.CefGlue.BrowserProcess.dll
    │   ├── coreclr.dll               # .NET runtime
    │   └── ... (all .NET runtime files)
    ├── GDCefGlue.dll                 # Plugin assembly
    ├── Xilium.CefGlue.BrowserProcess.exe  # Also in root
    ├── Xilium.CefGlue.BrowserProcess.dll  # Also in root
    ├── Xilium.CefGlue.dll
    ├── Xilium.CefGlue.Common.dll
    ├── Xilium.CefGlue.Common.Shared.dll
    ├── libcef.dll                    # CEF core
    ├── chrome_100_percent.pak
    ├── chrome_200_percent.pak
    ├── resources.pak
    └── ... (other CEF files)
```

---

## Common Issues

### Q: Export crashes with "GPU process isn't usable"

**A:** Ensure `Xilium.CefGlue.BrowserProcess.dll` is copied to the export directory. This is the most common issue.

### Q: Export crashes with "Network service crashed"

**A:** Same as above, ensure all BrowserProcess files are copied.

### Q: Page shows blank or cannot load

**A:** Check if `locales` directory exists and `resources.pak` file is copied.
