using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
        /// CEF 缓存目录。可在首次调用 Initialize() 前修改。
        /// 默认: user://cef_cache
        /// 可使用 user://，res://，或绝对路径。
        /// </summary>
        public static string CacheDirectory { get; set; } = "user://cef_cache";

        /// <summary>
        /// 在非 Windows 平台上为 true，表示 CEF 运行在外部消息循环模式下，
        /// 需要由宿主程序定期调用 CefRuntime.DoMessageLoopWork() 驱动 CEF 消息循环。
        /// </summary>
        public static bool UseExternalMessageLoop { get; private set; }

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
                var cachePath = ProjectSettings.GlobalizePath(CacheDirectory);
                Directory.CreateDirectory(cachePath);

                var resourcesDirPath = FindResourcesDirPath();
                var localesDirPath = Path.Combine(resourcesDirPath, "locales");

                // Linux/macOS 不支持 MultiThreadedMessageLoop（Windows 专用）。
                // 在非 Windows 平台使用外部消息循环模式，由 CefGlueControl._Process 驱动 DoMessageLoopWork()。
                // 若在 Linux 上设为 true，CEF 会在初始化时触发 int3 (DCHECK) 崩溃。
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                var settings = new CefSettings
                {
                    CachePath = cachePath,
                    RootCachePath = cachePath,
                    WindowlessRenderingEnabled = true,
                    NoSandbox = true,
                    MultiThreadedMessageLoop = isWindows,
                    ExternalMessagePump = !isWindows,
                    UncaughtExceptionStackSize = 100,
                    RemoteDebuggingPort = 0,
                    LogSeverity = CefLogSeverity.Warning,
                    LogFile = Path.Combine(cachePath, "cef.log"),
                    ResourcesDirPath = resourcesDirPath,
                    LocalesDirPath = localesDirPath,
                    Locale = "zh-CN"
                };

                // 在非 Windows 平台，暴露外部消息循环标志给 CefGlueControl 使用
                UseExternalMessageLoop = !isWindows;

// Linux: 安装全局 X11 错误处理器，忽略 BadWindow 等嵌入窗口模式下的非致命错误
                if (!isWindows)
                {
                    X11Methods.InstallGlobalErrorHandler();
                }
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

        /// <summary>
        /// Searches for the browser subprocess executable in common locations.
        /// </summary>
        /// <returns>Path to the subprocess executable, or null if not found.</returns>
        private static string FindBrowserSubprocessPath()
        {
            var basePath = AppContext.BaseDirectory;
            
            // Determine the browser process filename based on platform
            string browserProcessFileName;
            switch (CefRuntime.Platform)
            {
                case CefRuntimePlatform.Windows:
                    browserProcessFileName = "Xilium.CefGlue.BrowserProcess.exe";
                    break;
                case CefRuntimePlatform.Linux:
                case CefRuntimePlatform.MacOS:
                default:
                    browserProcessFileName = "Xilium.CefGlue.BrowserProcess";
                    break;
            }
            
            var searchPaths = new List<string>
            {
                Path.Combine(basePath, "CefGlueBrowserProcess", browserProcessFileName),
                Path.Combine(basePath, browserProcessFileName)
            };

            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(assemblyDir) && assemblyDir != basePath)
            {
                searchPaths.Add(Path.Combine(assemblyDir, "CefGlueBrowserProcess", browserProcessFileName));
                searchPaths.Add(Path.Combine(assemblyDir, browserProcessFileName));
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

            switch (CefRuntime.Platform)
            {
                case CefRuntimePlatform.Linux:
                    searchPaths.Add(Path.Combine(basePath, "runtimes", "linux-x64", "native"));
                    searchPaths.Add(Path.Combine(basePath, "..", "runtimes", "linux-x64", "native"));
                    break;
                case CefRuntimePlatform.MacOS:
                    searchPaths.Add(Path.Combine(basePath, "runtimes", "osx-x64", "native"));
                    searchPaths.Add(Path.Combine(basePath, "..", "runtimes", "osx-x64", "native"));
                    searchPaths.Add(Path.Combine(basePath, "Resources"));
                    break;
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

            return basePath;
        }
    }
}
