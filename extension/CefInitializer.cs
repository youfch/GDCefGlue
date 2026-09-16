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
        var port = ProjectSettings.Singleton.GetSetting(RemoteDebuggingPortSetting, 0).AsInt32();

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
    /// 只在编辑器进程内调用才有意义：GDExtension 构建由 Main.InitializeCefGlueTypes
    /// 在 Engine.Singleton.IsEditorHint() 为真时调用（不需要 plugin.cfg，也不需要手动启用）。
    ///
    /// 本方法只操作 ProjectSettings，不碰 CEF。
    /// </summary>
    public static void RegisterProjectSettings()
    {
        // 顺序不可颠倒：AddPropertyInfo 内部有 ERR_FAIL_COND(!props.has(name))，
        // 必须先把 key 建出来，否则类型/范围提示会被静默丢弃。
        if (!ProjectSettings.Singleton.HasSetting(RemoteDebuggingPortSetting))
            ProjectSettings.Singleton.SetSetting(RemoteDebuggingPortSetting, 0);

        ProjectSettings.Singleton.AddPropertyInfo(new Godot.Collections.GodotDictionary
        {
            { "name",        RemoteDebuggingPortSetting },
            { "type",        (int)VariantType.Int },
            { "hint",        (int)PropertyHint.Range },
            { "hint_string", "0,65535,1" },
            // 不要传 "usage"：Godot 4.5+ 会警告该键已不受支持，
            // 用量信息请用 SetAsBasic/SetRestartIfChanged/SetAsInternal。
        });

        // 初始值 = 0，且刻意不调用 SetAsBasic：
        // 调试后门藏在 "Advanced Settings" 后面是期望行为。
        // 值等于初始值，因此不会污染 project.godot（保存时会被剔除）。
        ProjectSettings.Singleton.SetInitialValue(RemoteDebuggingPortSetting, 0);

        // CEF 每进程只初始化一次，改动必须重启。
        ProjectSettings.Singleton.SetRestartIfChanged(RemoteDebuggingPortSetting, true);
    }

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
                ResourcesDirPath = resourcesDir,
                LocalesDirPath = localesDir,
                Locale = "zh-CN"
            };

            // 在非 Windows 平台，暴露外部消息循环标志给 CefGlueControl 使用
            UseExternalMessageLoop = !isWindows;

            // Linux: 安装全局 X11 错误处理器，忽略 BadWindow 等嵌入窗口模式下的非致命错误
            if (!isWindows)
            {
                X11Methods.InstallGlobalErrorHandler();
            }

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