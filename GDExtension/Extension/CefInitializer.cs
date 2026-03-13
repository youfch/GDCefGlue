using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

public static class CefInitializer
{
    private static bool _initialized;
    private static GodotBrowserProcessHandler _browserProcessHandler;
    private static string _extensionDirectory;

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static string CefLibraryName => IsWindows ? "libcef.dll" : IsLinux ? "libcef.so" : "libcef.dylib";
    
    private static string BrowserSubprocessName => IsWindows ? "Xilium.CefGlue.BrowserProcess.exe" : "Xilium.CefGlue.BrowserProcess";

    private static string RuntimeIdentifier
    {
        get
        {
            if (IsWindows) return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            if (IsLinux) return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            if (IsMacOS) return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            return "unknown";
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            GD.Print("CefInitializer: Starting CEF initialization...");
            GD.Print($"CefInitializer: Platform = {RuntimeIdentifier}");

            _extensionDirectory = GetExtensionDirectory();
            GD.Print($"CefInitializer: ExtensionDirectory = {_extensionDirectory}");

            var cachePath = Path.Combine(Godot.OS.Singleton.GetUserDataDir(), "cef_cache");
            Directory.CreateDirectory(cachePath);

            var resourcesDirPath = FindResourcesDirPath();
            var localesDirPath = Path.Combine(resourcesDirPath, "locales");

            GD.Print($"CefInitializer: CachePath = {cachePath}");
            GD.Print($"CefInitializer: ResourcesDirPath = {resourcesDirPath}");
            GD.Print($"CefInitializer: LocalesDirPath = {localesDirPath}");

            var cefLibraryPath = FindCefLibraryPath();
            if (cefLibraryPath == null)
            {
                GD.PrintErr($"CefInitializer: {CefLibraryName} not found!");
                return;
            }
            GD.Print($"CefInitializer: CefLibraryPath = {cefLibraryPath}");

            PreloadCefDependencies(_extensionDirectory);

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
                ResourcesDirPath = resourcesDirPath,
                LocalesDirPath = localesDirPath,
                Locale = "zh-CN"
            };

            var libcefHandle = NativeLibrary.Load(cefLibraryPath);
            GD.Print($"CefInitializer: {CefLibraryName} loaded, handle = {libcefHandle}");

            CefRuntime.Load();
            GD.Print("CefInitializer: CefRuntime.Load() completed");
            GD.Print($"CefInitializer: CEF Platform = {CefRuntime.Platform}");

            var subProcessPath = FindBrowserSubprocessPath();
            if (subProcessPath == null)
            {
                GD.PrintErr("CefInitializer: Browser subprocess not found!");
                return;
            }
            settings.BrowserSubprocessPath = subProcessPath;
            GD.Print($"CefInitializer: BrowserSubprocessPath = {subProcessPath}");

            var exeFileName = Process.GetCurrentProcess().MainModule?.FileName ?? "Godot";
            GD.Print($"CefInitializer: Main process = {exeFileName}");

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

    private static void PreloadCefDependencies(string directory)
    {
        if (IsWindows)
        {
            var dllFiles = new[]
            {
                "libcef.dll",
                "chrome_elf.dll",
                "d3dcompiler_47.dll",
                "libEGL.dll",
                "libGLESv2.dll",
                "vk_swiftshader.dll",
                "vulkan-1.dll"
            };

            foreach (var dll in dllFiles)
            {
                var dllPath = Path.Combine(directory, dll);
                if (File.Exists(dllPath))
                {
                    try
                    {
                        var handle = NativeLibrary.Load(dllPath);
                        GD.Print($"CefInitializer: Preloaded {dll}");
                    }
                    catch (Exception ex)
                    {
                        GD.Print($"CefInitializer: Failed to preload {dll}: {ex.Message}");
                    }
                }
            }
        }
        else if (IsLinux)
        {
            var soFiles = new[]
            {
                "libcef.so",
                "libvulkan.so.1",
                "libvk_swiftshader.so"
            };

            foreach (var so in soFiles)
            {
                var soPath = Path.Combine(directory, so);
                if (File.Exists(soPath))
                {
                    try
                    {
                        var handle = NativeLibrary.Load(soPath);
                        GD.Print($"CefInitializer: Preloaded {so}");
                    }
                    catch (Exception ex)
                    {
                        GD.Print($"CefInitializer: Failed to preload {so}: {ex.Message}");
                    }
                }
            }
        }
        else if (IsMacOS)
        {
            var frameworkPath = Path.Combine(directory, "Chromium Embedded Framework.framework", "Chromium Embedded Framework");
            if (File.Exists(frameworkPath))
            {
                try
                {
                    var handle = NativeLibrary.Load(frameworkPath);
                    GD.Print($"CefInitializer: Preloaded Chromium Embedded Framework");
                }
                catch (Exception ex)
                {
                    GD.Print($"CefInitializer: Failed to preload framework: {ex.Message}");
                }
            }
        }
    }

    private static string GetExtensionDirectory()
    {
        var projectPath = Godot.ProjectSettings.Singleton.GlobalizePath("res://");
        if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
        {
            var libPath = Path.Combine(projectPath, "lib");
            if (Directory.Exists(libPath))
            {
                var dllPath = Path.Combine(libPath, "GDCefGlueExtension.dll");
                var soPath = Path.Combine(libPath, "GDCefGlueExtension.so");
                var dylibPath = Path.Combine(libPath, "GDCefGlueExtension.dylib");
                if (File.Exists(dllPath) || File.Exists(soPath) || File.Exists(dylibPath))
                {
                    GD.Print($"CefInitializer: Found extension at: {libPath}");
                    return libPath;
                }
            }
            
            var addonsPath = Path.Combine(projectPath, "addons", "GCefGlue");
            if (Directory.Exists(addonsPath))
            {
                GD.Print($"CefInitializer: Found extension at: {addonsPath}");
                return addonsPath;
            }
        }

        var baseDirectory = AppContext.BaseDirectory;
        GD.Print($"CefInitializer: Using base directory: {baseDirectory}");
        return baseDirectory;
    }

    private static string FindCefLibraryPath()
    {
        var searchPaths = new List<string>
        {
            Path.Combine(_extensionDirectory, CefLibraryName),
            Path.Combine(_extensionDirectory, "runtimes", RuntimeIdentifier, "native", CefLibraryName),
            Path.Combine(AppContext.BaseDirectory, CefLibraryName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeIdentifier, "native", CefLibraryName)
        };

        if (IsMacOS)
        {
            searchPaths.Insert(0, Path.Combine(_extensionDirectory, "Chromium Embedded Framework.framework", "Chromium Embedded Framework"));
            searchPaths.Insert(1, Path.Combine(_extensionDirectory, "Frameworks", "Chromium Embedded Framework.framework", "Chromium Embedded Framework"));
        }

        foreach (var path in searchPaths)
        {
            GD.Print($"CefInitializer: Checking {CefLibraryName} path: {path}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string FindBrowserSubprocessPath()
    {
        var searchPaths = new List<string>
        {
            Path.Combine(_extensionDirectory, "CefGlueBrowserProcess", BrowserSubprocessName),
            Path.Combine(_extensionDirectory, BrowserSubprocessName),
            Path.Combine(AppContext.BaseDirectory, "CefGlueBrowserProcess", BrowserSubprocessName),
            Path.Combine(AppContext.BaseDirectory, BrowserSubprocessName)
        };

        foreach (var path in searchPaths)
        {
            GD.Print($"CefInitializer: Checking subprocess path: {path}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string FindResourcesDirPath()
    {
        var searchPaths = new List<string>
        {
            _extensionDirectory,
            Path.Combine(_extensionDirectory, "runtimes", RuntimeIdentifier, "native"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeIdentifier, "native")
        };

        foreach (var path in searchPaths)
        {
            var pakFile = Path.Combine(path, "resources.pak");
            var localesDir = Path.Combine(path, "locales");
            if (File.Exists(pakFile) && Directory.Exists(localesDir))
            {
                GD.Print($"CefInitializer: Found resources at: {path}");
                return path;
            }
        }

        GD.Print($"CefInitializer: Using fallback resources path: {_extensionDirectory}");
        return _extensionDirectory;
    }
}
