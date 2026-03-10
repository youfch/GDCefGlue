using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    internal class GodotCefApp : CefApp
    {
        private readonly GodotBrowserProcessHandler _browserProcessHandler;

        public GodotCefApp()
        {
            _browserProcessHandler = new GodotBrowserProcessHandler();
        }

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
                
                commandLine.AppendSwitch("enable-begin-frame-scheduling");
                commandLine.AppendSwitch("disable-smooth-scrolling");
                
                GD.Print($"GodotCefApp: Command line switches added (GPU Acceleration: {CefGlueControl.UseGpuAcceleration})");
            }
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler()
        {
            return _browserProcessHandler;
        }
    }
}
