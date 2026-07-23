using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public static class CefInitializer
{
    private static bool _initialized;
    private static GodotBrowserProcessHandler _browserProcessHandler;
    private static string _addonRoot;

    public static string CacheDirectory { get; set; } = "user://cef_cache";

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            GD.Print("CefInitializer: Starting CEF initialization...");

            _addonRoot = ResolveAddonRoot();
            var platform = DetectPlatform();
            var platformDir = Path.Combine(_addonRoot, platform);
            var cachePath = ProjectSettings.Singleton.GlobalizePath(CacheDirectory);
            Directory.CreateDirectory(cachePath);

            // CEF native library (libcef.dll / .so / .dylib)
            var cefLibraryPath = FindCefLibrary(platformDir);
            if (cefLibraryPath == null)
            {
                GD.PrintErr("CefInitializer: libcef not found!");
                return;
            }

            // Preload CEF DLLs (Windows only)
            PreloadCefDependencies(platformDir);

            // Resources (resources.pak, locales/)
            var resourcesDir = FindResources(platformDir);
            var localesDir = Path.Combine(resourcesDir, "locales");

            var settings = new CefSettings
            {
                CachePath = cachePath,
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                NoSandbox = true,
                MultiThreadedMessageLoop = true,
                UncaughtExceptionStackSize = 100,
                RemoteDebuggingPort = 0,
                LogSeverity = CefLogSeverity.Warning,
                LogFile = Path.Combine(cachePath, "cef.log"),
                ResourcesDirPath = resourcesDir,
                LocalesDirPath = localesDir,
                Locale = "zh-CN"
            };

            var libcefHandle = NativeLibrary.Load(cefLibraryPath);
            if (libcefHandle == IntPtr.Zero) { GD.PrintErr("CefInitializer: Failed to load libcef"); return; }

            CefRuntime.Load();

            var subProcessPath = FindBrowserSubprocess(platformDir);
            if (subProcessPath == null)
            {
                GD.PrintErr("CefInitializer: Browser subprocess not found!");
                return;
            }
            settings.BrowserSubprocessPath = subProcessPath;

            var exeFileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Godot";
            _browserProcessHandler = new GodotBrowserProcessHandler();

            CefRuntime.Initialize(new CefMainArgs(new[] { exeFileName }), settings, new GodotCefApp(), IntPtr.Zero);
            GD.Print($"CefInitializer: CEF initialized. IsInitialized = {CefRuntime.IsInitialized}");

            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                GD.Print("CefInitializer: Shutting down CEF...");
                CefRuntime.Shutdown();
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"CefInitializer: Failed - {ex.GetType().Name}: {ex.Message}");
            GD.PrintErr($"Stack: {ex.StackTrace}");
        }
    }

    private static string DetectPlatform()
    {
        var isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        var isX64 = RuntimeInformation.OSArchitecture == Architecture.X64;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return isArm64 ? "windows-arm64" : "windows-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return isArm64 ? "linux-arm64" : "linux-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return isArm64 ? "macos-arm64" : "macos-x64";

        return "windows-x64";
    }

    private static string ResolveAddonRoot()
    {
        // 1. res://addons/gdcefglue/ (editor/dev, GDExtension release package)
        var projectPath = ProjectSettings.Singleton.GlobalizePath("res://");
        var addonsPath = Path.Combine(projectPath, "addons", "gdcefglue");
        if (Directory.Exists(addonsPath))
            return addonsPath;

        // 2. lib/ (test project)
        var libPath = Path.Combine(projectPath, "lib");
        if (Directory.Exists(libPath))
            return libPath;

        // 3. Fallback
        return AppContext.BaseDirectory;
    }

    private static string FindCefLibrary(string platformDir)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var fileName = isWindows ? "libcef.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libcef.so"
            : "libcef.dylib";

        var paths = new List<string>
        {
            Path.Combine(platformDir, fileName),
            Path.Combine(AppContext.BaseDirectory, fileName)
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void PreloadCefDependencies(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string[] dllFiles = { "libcef.dll", "chrome_elf.dll", "d3dcompiler_47.dll", "libEGL.dll", "libGLESv2.dll", "vk_swiftshader.dll", "vulkan-1.dll" };
        foreach (var dll in dllFiles)
        {
            var dllPath = Path.Combine(directory, dll);
            if (File.Exists(dllPath))
            {
                try { NativeLibrary.Load(dllPath); } catch { }
            }
        }
    }

    private static string FindBrowserSubprocess(string platformDir)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var fileName = isWindows ? "Xilium.CefGlue.BrowserProcess.exe" : "Xilium.CefGlue.BrowserProcess";

        var paths = new List<string>
        {
            Path.Combine(platformDir, "CefGlueBrowserProcess", fileName),
            Path.Combine(platformDir, fileName),
            Path.Combine(AppContext.BaseDirectory, "CefGlueBrowserProcess", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName)
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string FindResources(string platformDir)
    {
        var paths = new List<string>
        {
            platformDir,
            AppContext.BaseDirectory
        };

        foreach (var path in paths)
        {
            var pakFile = Path.Combine(path, "resources.pak");
            var localesDir = Path.Combine(path, "locales");
            if (File.Exists(pakFile) && Directory.Exists(localesDir))
                return path;
        }

        return platformDir;
    }
}