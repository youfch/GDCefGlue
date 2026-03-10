using System.Diagnostics;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    internal class GodotBrowserProcessHandler : CefBrowserProcessHandler
    {
        private readonly string _currentProcessId;

        public GodotBrowserProcessHandler()
        {
            _currentProcessId = Process.GetCurrentProcess().Id.ToString();
        }

        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine)
        {
            commandLine.AppendSwitch("--parent-pid", _currentProcessId);
            GD.Print($"GodotBrowserProcessHandler: OnBeforeChildProcessLaunch, parent-pid = {_currentProcessId}");
        }

        protected override void OnContextInitialized()
        {
            GD.Print("GodotBrowserProcessHandler: OnContextInitialized");
        }

        protected override void OnScheduleMessagePumpWork(long delayMs)
        {
        }
    }
}
