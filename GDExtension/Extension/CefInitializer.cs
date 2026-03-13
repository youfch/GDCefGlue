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

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            GD.Print("CefInitializer: Starting CEF initialization...");

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
                GD.PrintErr("CefInitializer: libcef not found!");
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
            GD.Print($"CefInitializer: libcef loaded, handle = {libcefHandle}");

            CefRuntime.Load();
            GD.Print("CefInitializer: CefRuntime.Load() completed");
            GD.Print($"CefInitializer: Platform = {CefRuntime.Platform}");

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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string[] dllFiles;
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            dllFiles = new[]
            {
                "libcef.dll",
                "chrome_elf.dll",
                "d3dcompiler_47.dll",
                "libEGL.dll",
                "libGLESv2.dll",
                "vk_swiftshader.dll",
                "vulkan-1.dll"
            };
        }
        else
        {
            return;
        }

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

    private static string GetExtensionDirectory()
    {
        var projectPath = Godot.ProjectSettings.Singleton.GlobalizePath("res://");
        if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
        {
            var libPath = Path.Combine(projectPath, "lib");
            if (Directory.Exists(libPath))
            {
                var dllPath = Path.Combine(libPath, "GDCefGlueExtension.dll");
                if (File.Exists(dllPath))
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
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        
        var cefLibNames = new List<string>();
        
        if (isWindows)
        {
            cefLibNames.Add("libcef.dll");
            cefLibNames.Add(Path.Combine("runtimes", "win-x64", "native", "libcef.dll"));
        }
        else if (isLinux)
        {
            cefLibNames.Add("libcef.so");
            cefLibNames.Add("cef.so");
            cefLibNames.Add(Path.Combine("runtimes", "linux-x64", "native", "libcef.so"));
            cefLibNames.Add(Path.Combine("runtimes", "linux-x64", "native", "cef.so"));
        }
        else if (isMac)
        {
            cefLibNames.Add("libcef.dylib");
            cefLibNames.Add("cef.dylib");
            cefLibNames.Add(Path.Combine("runtimes", "osx-x64", "native", "libcef.dylib"));
        }

        var searchPaths = new List<string>();
        
        foreach (var cefLib in cefLibNames)
        {
            searchPaths.Add(Path.Combine(_extensionDirectory, cefLib));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, cefLib));
        }

        foreach (var path in searchPaths)
        {
            GD.Print($"CefInitializer: Checking libcef path: {path}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string FindBrowserSubprocessPath()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        var browserProcessFileName = isWindows 
            ? "Xilium.CefGlue.BrowserProcess.exe" 
            : "Xilium.CefGlue.BrowserProcess";

        var searchPaths = new List<string>
        {
            Path.Combine(_extensionDirectory, "CefGlueBrowserProcess", browserProcessFileName),
            Path.Combine(_extensionDirectory, browserProcessFileName),
            Path.Combine(AppContext.BaseDirectory, "CefGlueBrowserProcess", browserProcessFileName),
            Path.Combine(AppContext.BaseDirectory, browserProcessFileName)
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
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        var searchPaths = new List<string>
        {
            _extensionDirectory,
            AppContext.BaseDirectory
        };

        if (isWindows)
        {
            searchPaths.Add(Path.Combine(_extensionDirectory, "runtimes", "win-x64", "native"));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native"));
        }
        else if (isLinux)
        {
            searchPaths.Add(Path.Combine(_extensionDirectory, "runtimes", "linux-x64", "native"));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native"));
        }
        else if (isMac)
        {
            searchPaths.Add(Path.Combine(_extensionDirectory, "runtimes", "osx-x64", "native"));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-x64", "native"));
            searchPaths.Add(Path.Combine(_extensionDirectory, "Resources"));
            searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "Resources"));
        }

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
