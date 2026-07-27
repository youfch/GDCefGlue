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
            }

            // GPU 相关开关需要应用到所有进程类型（包括 gpu-process），
            // 否则 --no-zygote 模式下 GPU 子进程仍会尝试硬件加速并崩溃。
            if (!CefGlueControl.UseGpuAcceleration)
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
