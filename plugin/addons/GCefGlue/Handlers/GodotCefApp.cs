using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// CEF application class that handles command line processing and provides the browser process handler.
    /// </summary>
    internal class GodotCefApp : CefApp
    {
        private readonly GodotBrowserProcessHandler _browserProcessHandler;

        public GodotCefApp()
        {
            _browserProcessHandler = new GodotBrowserProcessHandler();
        }

        /// <summary>
        /// Called before command line processing. Configures GPU acceleration and other settings.
        /// </summary>
        /// <param name="processType">The process type, empty for the main browser process.</param>
        /// <param name="commandLine">The command line to modify.</param>
        protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
        {
            // Linux 上 zygote 进程会触发 int3 (DCHECK) 崩溃，需要禁用 zygote。
            // 参考 OutSystems/CefGlue BrowserCefApp.cs 的 Linux 处理。
            if (CefRuntime.Platform == CefRuntimePlatform.Linux)
            {
                commandLine.AppendSwitch("no-zygote");

                // 强制使用 X11 后端，禁用 Wayland Ozone 后端。
                // 在 XWayland 环境下，Godot 报告 DisplayServer name 为 "X11"，
                // 但 CEF 内部会检测到 Wayland 并使用 Ozone Wayland 后端，
                // 导致 CEF 创建的 Wayland surface 无法嵌入到 XWayland 窗口中。
                // 强制 --ozone-platform=x11 让 CEF 使用 X11 后端，与 Godot 一致。
                commandLine.AppendSwitch("ozone-platform", "x11");
            }

            // GPU 相关开关需要应用到所有进程类型（包括 gpu-process），
            // 否则 --no-zygote 模式下 GPU 子进程仍会尝试硬件加速并崩溃。
            if (!CefGlueControl.UseGpuAcceleration)
            {
                commandLine.AppendSwitch("disable-gpu");
                commandLine.AppendSwitch("disable-gpu-compositing");
                commandLine.AppendSwitch("use-angle", "swiftshader");
            }

            // 嵌入窗口模式在 Linux/XWayland 下需要禁用 GPU 合成，
            // 否则 CEF 的 GPU 进程创建 CommandBuffer 失败导致内容不渲染。
            // 报错: ContextResult::kTransientFailure: Failed to send GpuControl.CreateCommandBuffer
            if (CefRuntime.Platform == CefRuntimePlatform.Linux)
            {
                commandLine.AppendSwitch("disable-gpu");
                commandLine.AppendSwitch("disable-gpu-compositing");
                commandLine.AppendSwitch("use-angle", "swiftshader");
            }

            if (string.IsNullOrEmpty(processType))
            {
                if (CefGlueControl.UseTransparent)
                {
                    commandLine.AppendSwitch("enable-begin-frame-scheduling");
                }
                
                commandLine.AppendSwitch("disable-smooth-scrolling");
                commandLine.AppendSwitch("allow-file-access-from-files");
                commandLine.AppendSwitch("allow-universal-access-from-files");
            }
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler()
        {
            return _browserProcessHandler;
        }
    }
}
