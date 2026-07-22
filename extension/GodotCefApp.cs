using Godot;
using Xilium.CefGlue;

namespace GDCefGlueExtension;

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
