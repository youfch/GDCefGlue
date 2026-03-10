using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Static class responsible for initializing and configuring the CEF runtime.
    /// Should be called once before creating any browser instances.
    /// </summary>
    public static class CefInitializer
    {
        private static bool _initialized;
        private static GodotBrowserProcessHandler _browserProcessHandler;

        /// <summary>
        /// Initializes the CEF runtime with default settings.
        /// This method is idempotent - subsequent calls will be ignored.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                GD.Print("CefInitializer: Starting CEF initialization...");

                var basePath = AppContext.BaseDirectory;
                var cachePath = Path.Combine(OS.GetUserDataDir(), "cef_cache");
                Directory.CreateDirectory(cachePath);

                var resourcesDirPath = FindResourcesDirPath();
                var localesDirPath = Path.Combine(resourcesDirPath, "locales");

                GD.Print($"CefInitializer: BasePath = {basePath}");
                GD.Print($"CefInitializer: CachePath = {cachePath}");
                GD.Print($"CefInitializer: ResourcesDirPath = {resourcesDirPath}");

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

        /// <summary>
        /// Searches for the browser subprocess executable in common locations.
        /// </summary>
        /// <returns>Path to the subprocess executable, or null if not found.</returns>
        private static string FindBrowserSubprocessPath()
        {
            var basePath = AppContext.BaseDirectory;
            var searchPaths = new List<string>
            {
                Path.Combine(basePath, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess.exe"),
                Path.Combine(basePath, "Xilium.CefGlue.BrowserProcess.exe")
            };

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(assemblyDir) && assemblyDir != basePath)
            {
                searchPaths.Add(Path.Combine(assemblyDir, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess.exe"));
                searchPaths.Add(Path.Combine(assemblyDir, "Xilium.CefGlue.BrowserProcess.exe"));
            }

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

        /// <summary>
        /// Locates the CEF resources directory containing resources.pak and locales.
        /// </summary>
        /// <returns>Path to the resources directory.</returns>
        private static string FindResourcesDirPath()
        {
            var basePath = AppContext.BaseDirectory;
            
            var searchPaths = new List<string>
            {
                basePath,
                Path.Combine(basePath, "runtimes", "win-x64", "native"),
                Path.Combine(basePath, "..", "runtimes", "win-x64", "native")
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

            GD.Print($"CefInitializer: Using fallback resources path: {basePath}");
            return basePath;
        }
    }
}
