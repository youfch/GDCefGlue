using System.Diagnostics;
using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles browser process-level events from CEF.
    /// </summary>
    internal class GodotBrowserProcessHandler : CefBrowserProcessHandler
    {
        private readonly string _currentProcessId;

        public GodotBrowserProcessHandler()
        {
            _currentProcessId = Process.GetCurrentProcess().Id.ToString();
        }

        /// <summary>
        /// Called before a child process is launched. Adds parent process ID for debugging.
        /// </summary>
        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine)
        {
            commandLine.AppendSwitch("--parent-pid", _currentProcessId);
            GD.Print($"GodotBrowserProcessHandler: OnBeforeChildProcessLaunch, parent-pid = {_currentProcessId}");
        }

        /// <summary>
        /// Called when the browser context has been initialized.
        /// </summary>
        protected override void OnContextInitialized()
        {
            GD.Print("GodotBrowserProcessHandler: OnContextInitialized");
        }

        /// <summary>
        /// Called to schedule work on the browser message pump.
        /// </summary>
        protected override void OnScheduleMessagePumpWork(long delayMs)
        {
        }
    }
}
