using Godot;
using Xilium.CefGlue;

namespace GDCefGlue
{
    /// <summary>
    /// Handles browser lifecycle events from CEF.
    /// Manages browser creation and popup window behavior.
    /// </summary>
    internal class GodotLifeSpanHandler : CefLifeSpanHandler
    {
        private readonly CefGlueControl _control;

        public GodotLifeSpanHandler(CefGlueControl control)
        {
            _control = control;
        }

        /// <summary>
        /// Called after a browser has been created.
        /// </summary>
        protected override void OnAfterCreated(CefBrowser browser)
        {
            _control.OnBrowserCreated(browser);
        }

        /// <summary>
        /// Called before a popup window is created.
        /// Can redirect popups to the current browser based on settings.
        /// </summary>
        /// <returns>True to cancel the popup, false to allow it.</returns>
        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess)
        {
            GD.Print($"GodotLifeSpanHandler: OnBeforePopup - {targetUrl}");
            
            if (_control.OpenPopupInCurrentBrowser)
            {
                switch (targetDisposition)
                {
                    case CefWindowOpenDisposition.NewBackgroundTab:
                    case CefWindowOpenDisposition.NewForegroundTab:
                    case CefWindowOpenDisposition.NewWindow:
                    case CefWindowOpenDisposition.NewPopup:
                        _control.CallDeferred("NavigateToUrl", targetUrl);
                        return true;
                }
            }
            
            return false;
        }
    }
}
