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
            if (string.IsNullOrEmpty(processType))
            {
                if (!CefGlueControl.UseGpuAcceleration)
                {
                    commandLine.AppendSwitch("disable-gpu");
                    commandLine.AppendSwitch("disable-gpu-compositing");
                    commandLine.AppendSwitch("use-angle", "swiftshader");
                }
                
                if (CefGlueControl.UseTransparent)
                {
                    commandLine.AppendSwitch("enable-begin-frame-scheduling");
                }
                
                commandLine.AppendSwitch("disable-smooth-scrolling");
                commandLine.AppendSwitch("allow-file-access-from-files");
                commandLine.AppendSwitch("allow-universal-access-from-files");
                
                GD.Print($"GodotCefApp: Command line switches added (GPU Acceleration: {CefGlueControl.UseGpuAcceleration}, Transparent: {CefGlueControl.UseTransparent})");
            }
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler()
        {
            return _browserProcessHandler;
        }
    }
}
