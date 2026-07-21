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

        /// <summary>
        /// CEF 缓存目录。可在首次调用 Initialize() 前修改。
        /// 默认: user://cef_cache
        /// </summary>
        public static string CacheDirectory { get; set; } = "user://cef_cache";

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                GD.Print("CefInitializer: Starting CEF initialization...");

                _extensionDirectory = GetExtensionDirectory();

                var cachePath = ProjectSettings.Singleton.GlobalizePath(CacheDirectory);
                Directory.CreateDirectory(cachePath);

            var resourcesDirPath = FindResourcesDirPath();
            var localesDirPath = Path.Combine(resourcesDirPath, "locales");

            var cefLibraryPath = FindCefLibraryPath();
            if (cefLibraryPath == null)

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
            if (libcefHandle == IntPtr.Zero) { GD.PrintErr("CefInitializer: Failed to load libcef"); return; }

            CefRuntime.Load();

            var subProcessPath = FindBrowserSubprocessPath();
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

    private static void PreloadCefDependencies(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string[] dllFiles = new[]
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
                try { NativeLibrary.Load(dllPath); }
                catch { }
            }
        }
    }

    private static string GetExtensionDirectory()
    {
        var projectPath = Godot.ProjectSettings.Singleton.GlobalizePath("res://");
        if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
        {
            // Check addons/gdcefglue (GDExtension release package)
            var gdeAddonsPath = Path.Combine(projectPath, "addons", "gdcefglue");
            if (Directory.Exists(gdeAddonsPath))
            {
                return gdeAddonsPath;
            }

            var libPath = Path.Combine(projectPath, "lib");
            if (Directory.Exists(libPath))
            {
                var dllPath = Path.Combine(libPath, "GDCefGlueExtension.dll");
                if (File.Exists(dllPath))
                {
                    return libPath;
                }
            }
            
            var addonsPath = Path.Combine(projectPath, "addons", "GCefGlue");
            if (Directory.Exists(addonsPath))
            {
                return addonsPath;
            }
        }

        var baseDirectory = AppContext.BaseDirectory;
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
                return path;
            }
        }

        return _extensionDirectory;
    }
}
