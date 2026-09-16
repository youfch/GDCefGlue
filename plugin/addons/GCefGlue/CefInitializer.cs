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
        /// 远程调试端口对应的 Godot 项目设置 key。0 = 关闭。
        /// </summary>
        public const string RemoteDebuggingPortSetting = "gdcefglue/remote_debugging_port";

        /// <summary>
        /// 本次进程实际生效的 CEF 远程调试端口（Chrome DevTools Protocol）。0 = 关闭。
        ///
        /// 必须是进程级单一值：CEF 每个进程只有一个 CDP 端点，多个 CefBrowser
        /// 在该端口下表现为多个 target，无法各自占用端口。
        /// 因此它不能做成 CefGlueControl 的 Export 属性。
        /// </summary>
        public static int RemoteDebuggingPort { get; private set; }

        /// <summary>
        /// 从 Godot 项目设置读取远程调试端口。
        ///
        /// 用项目设置而非环境变量的原因：可版本控制、可在面板里发现、
        /// 且不依赖启动方式（编辑器/导出/godot 命令行都能生效）。
        /// </summary>
        private static int ResolveRemoteDebuggingPort()
        {
            // key 不存在时 GetSetting 静默返回默认值，不会打印告警
            var port = ProjectSettings.GetSetting(RemoteDebuggingPortSetting, 0).AsInt32();

            if (port == 0)
                return 0; // 显式关闭（默认）

            // CEF 仅在 [1024, 65535] 内才把该值翻译成 --remote-debugging-port，
            // 越界会被静默忽略（服务根本不启动），所以这里必须显式告警。
            if (port < 1024 || port > 65535)
            {
                GD.PushWarning(
                    $"[CefInitializer] 忽略越界的 {RemoteDebuggingPortSetting}={port}，有效范围 1024-65535");
                return 0;
            }

            GD.Print($"[CefInitializer] 远程调试已开启: 127.0.0.1:{port}" +
                     "（仅本机可访问、无鉴权；用 chrome://inspect 或 /json/list 连接）");
            return port;
        }

        /// <summary>
        /// 把 gdcefglue/* 注册进 Project Settings 面板，使其带正确的类型与范围提示。
        ///
        /// 只在编辑器进程内调用才有意义：
        /// - C# 插件构建：由 GDCefGlueSettingsPlugin(EditorPlugin)._EnterTree 调用
        /// - GDExtension 构建：由扩展初始化在编辑器进程内调用
        ///
        /// 本方法只操作 ProjectSettings，不碰 CEF。
        /// </summary>
        public static void RegisterProjectSettings()
        {
            // 顺序不可颠倒：AddPropertyInfo 内部有 ERR_FAIL_COND(!props.has(name))，
            // 必须先把 key 建出来，否则类型/范围提示会被静默丢弃。
            if (!ProjectSettings.HasSetting(RemoteDebuggingPortSetting))
                ProjectSettings.SetSetting(RemoteDebuggingPortSetting, 0);

            ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
            {
                { "name",        RemoteDebuggingPortSetting },
                { "type",        (int)Variant.Type.Int },
                { "hint",        (int)PropertyHint.Range },
                { "hint_string", "0,65535,1" },
                // 不要传 "usage"：Godot 4.5+ 会警告该键已不受支持，
                // 用量信息请用 SetAsBasic/SetRestartIfChanged/SetAsInternal。
            });

            // 初始值 = 0，且刻意不调用 SetAsBasic：
            // 调试后门藏在 "Advanced Settings" 后面是期望行为。
            // 值等于初始值，因此不会污染 project.godot（保存时会被剔除）。
            ProjectSettings.SetInitialValue(RemoteDebuggingPortSetting, 0);

            // CEF 每进程只初始化一次，改动必须重启。
            ProjectSettings.SetRestartIfChanged(RemoteDebuggingPortSetting, true);
        }

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

                // 必须先解析并赋值，再构造 CefSettings：
                // GodotCefApp.OnBeforeCommandLineProcessing 是在 CefRuntime.Initialize()
                // 内部被回调的，届时 RemoteDebuggingPort 必须已经可读。
                var remoteDebugPort = ResolveRemoteDebuggingPort();
                RemoteDebuggingPort = remoteDebugPort;

                var settings = new CefSettings
                {
                    CachePath = cachePath,
                    RootCachePath = cachePath,
                    WindowlessRenderingEnabled = true,
                    NoSandbox = true,
                    MultiThreadedMessageLoop = isWindows,
                    ExternalMessagePump = !isWindows,
                    UncaughtExceptionStackSize = 100,
                    RemoteDebuggingPort = remoteDebugPort,
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
