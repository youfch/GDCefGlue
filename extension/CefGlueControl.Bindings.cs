using Godot;
using Godot.Bridge;
using Godot.Collections;

namespace GDCefGlueExtension;

public partial class CefGlueControl
{
    internal static void BindMembers(ClassRegistrationContext context)
    {
        context.BindConstructor(() => new CefGlueControl());

        context.AddPropertyGroup("Browser Settings");
        context.BindProperty(new PropertyInfo(new StringName("initial_url"), VariantType.String) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.InitialUrl, static (CefGlueControl i, string v) => i.InitialUrl = v);
        context.BindProperty(new PropertyInfo(new StringName("mode"), VariantType.Int) { Hint = PropertyHint.Enum, HintString = "OSR,EmbeddedWindow", Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => (int)i.Mode, static (CefGlueControl i, int v) => i.Mode = (RenderMode)v);
        context.BindProperty(new PropertyInfo(new StringName("frame_rate"), VariantType.Int) { Hint = PropertyHint.Range, HintString = "1,360", Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.FrameRate, static (CefGlueControl i, int v) => i.FrameRate = v);
        context.BindProperty(new PropertyInfo(new StringName("transparent"), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.Transparent, static (CefGlueControl i, bool v) => i.Transparent = v);
        context.BindProperty(new PropertyInfo(new StringName("address"), VariantType.String) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.Address, static (CefGlueControl i, string v) => i.Address = v);

        context.AddPropertyGroup("Feature Toggles");
        context.BindProperty(new PropertyInfo(new StringName("gpu_acceleration"), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.GpuCompositing, static (CefGlueControl i, bool v) => i.GpuCompositing = v);
        context.BindProperty(new PropertyInfo(new StringName("open_popup_in_current_browser"), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.OpenPopupInCurrentBrowser, static (CefGlueControl i, bool v) => i.OpenPopupInCurrentBrowser = v);
        context.BindProperty(new PropertyInfo(new StringName("enable_media_stream"), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.EnableMediaStream, static (CefGlueControl i, bool v) => i.EnableMediaStream = v);

        // SyncCursor / ForwardInputEvents 通过 _GetPropertyList 动态添加

        context.BindMethod(new StringName("go_back"), (CefGlueControl i) => i.GoBack());
        context.BindMethod(new StringName("go_forward"), (CefGlueControl i) => i.GoForward());
        context.BindMethod(new StringName("navigate_to_url"), new ParameterInfo(new StringName("url"), VariantType.String),
            (CefGlueControl i, string url) => i.NavigateToUrl(url));
        context.BindMethod(new StringName("reload"), new ParameterInfo(new StringName("ignore_cache"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(false)),
            (CefGlueControl i, bool ignoreCache) => i.Reload(ignoreCache));
        context.BindMethod(new StringName("execute_javascript"),
            new ParameterInfo(new StringName("code"), VariantType.String),
            new ParameterInfo(new StringName("url"), VariantType.String, VariantTypeMetadata.None, Variant.CreateFrom("about:blank")),
            new ParameterInfo(new StringName("line"), VariantType.Int, VariantTypeMetadata.Int32, Variant.CreateFrom(1)),
            (CefGlueControl i, string code, string url, int line) => i.ExecuteJavaScript(code, url, line));
        context.BindMethod(new StringName("eval_js"), new ParameterInfo(new StringName("code"), VariantType.String),
            (CefGlueControl i, string code) => i.EvalJs(code));
        context.BindMethod(new StringName("register_js_handler"),
            new ParameterInfo(new StringName("name"), VariantType.String),
            new ParameterInfo(new StringName("handler"), VariantType.Callable),
            new ParameterInfo(new StringName("methods"), VariantType.String, VariantTypeMetadata.None, Variant.CreateFrom("[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]")),
            (CefGlueControl i, string name, Callable handler, string methods) => i.RegisterJsHandler(name, handler, methods));
        context.BindMethod(new StringName("unregister_js_handler"), new ParameterInfo(new StringName("name"), VariantType.String),
            (CefGlueControl i, string name) => i.UnregisterJsHandler(name));
        context.BindMethod(new StringName("send_to_js"), new ParameterInfo(new StringName("json"), VariantType.String),
            (CefGlueControl i, string json) => i.SendToJs(json));
        context.BindMethod(new StringName("send_response"),
            new ParameterInfo(new StringName("cb_id"), VariantType.String),
            new ParameterInfo(new StringName("json"), VariantType.String),
            (CefGlueControl i, string cbId, string json) => i.SendResponse(cbId, json));
        context.BindMethod(new StringName("show_developer_tools"), (CefGlueControl i) => i.ShowDeveloperTools());
        context.BindMethod(new StringName("close_developer_tools"), (CefGlueControl i) => i.CloseDeveloperTools());
        context.BindMethod(new StringName("find"),
            new ParameterInfo(new StringName("search_text"), VariantType.String),
            new ParameterInfo(new StringName("forward"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(true)),
            new ParameterInfo(new StringName("match_case"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(false)),
            new ParameterInfo(new StringName("find_next"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(false)),
            (CefGlueControl i, string searchText, bool forward, bool matchCase, bool findNext) => i.Find(searchText, forward, matchCase, findNext));
        context.BindMethod(new StringName("stop_finding"),
            new ParameterInfo(new StringName("clear_selection"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(true)),
            (CefGlueControl i, bool clearSelection) => i.StopFinding(clearSelection));
        context.BindMethod(new StringName("on_eval_done"),
            new ParameterInfo(new StringName("result"), VariantType.String),
            new ParameterInfo(new StringName("error"), VariantType.String),
            (CefGlueControl i, string result, string error) => i.OnEvalDone(result, error));

        context.BindMethod(new StringName("_create_browser_deferred"), (CefGlueControl i) => i.CreateBrowserDeferred());
        context.BindMethod(new StringName("_notify_browser_initialized"), (CefGlueControl i) => i.NotifyBrowserInitialized());
        context.BindMethod(new StringName("_notify_address_changed"), new ParameterInfo(new StringName("url"), VariantType.String),
            (CefGlueControl i, string url) => i._notify_address_changed(url));
        context.BindMethod(new StringName("_notify_title_changed"), new ParameterInfo(new StringName("title"), VariantType.String),
            (CefGlueControl i, string title) => i._notify_title_changed(title));
        context.BindMethod(new StringName("_notify_load_start"), (CefGlueControl i) => i._notify_load_start());
        context.BindMethod(new StringName("_notify_load_end"), (CefGlueControl i) => i._notify_load_end());
        context.BindMethod(new StringName("_notify_load_error"), new ParameterInfo(new StringName("errorText"), VariantType.String), new ParameterInfo(new StringName("failedUrl"), VariantType.String),
            (CefGlueControl i, string errorText, string failedUrl) => i._notify_load_error(errorText, failedUrl));
        context.BindMethod(new StringName("_notify_find_result"),
            new ParameterInfo(new StringName("identifier"), VariantType.Int),
            new ParameterInfo(new StringName("count"), VariantType.Int),
            new ParameterInfo(new StringName("activeMatchOrdinal"), VariantType.Int),
            new ParameterInfo(new StringName("finalUpdate"), VariantType.Bool),
            (CefGlueControl i, int identifier, int count, int activeMatchOrdinal, bool finalUpdate) => i._notify_find_result(identifier, count, activeMatchOrdinal, finalUpdate));

        // ── 右键菜单 deferred 方法 ──
        context.BindMethod(new StringName("_notify_run_context_menu"), (CefGlueControl i) => i._notify_run_context_menu());
        context.BindMethod(new StringName("_notify_context_menu_command"), (CefGlueControl i) => i._notify_context_menu_command());
        context.BindMethod(new StringName("_notify_context_menu_dismissed"), (CefGlueControl i) => i._notify_context_menu_dismissed());
        context.BindMethod(new StringName("_deferred_context_menu_popup_hide"), (CefGlueControl i) => i._deferred_context_menu_popup_hide());

        // ── IME deferred 方法 ──
        context.BindMethod(new StringName("_activate_ime"), (CefGlueControl i) => i._activate_ime());
        context.BindMethod(new StringName("_deactivate_ime"), (CefGlueControl i) => i._deactivate_ime());

        // ── 光标 deferred 方法 ──
        context.BindMethod(new StringName("UpdateCursorShape"), new ParameterInfo(new StringName("cefCursorType"), VariantType.Int),
            (CefGlueControl i, int cefCursorType) => i.UpdateCursorShape(cefCursorType));

        context.BindSignal(new SignalInfo(new StringName("browser_initialized")));
        context.BindSignal(new SignalInfo(new StringName("address_changed")));
        context.BindSignal(new SignalInfo(new StringName("title_changed")));
        context.BindSignal(new SignalInfo(new StringName("load_start")));
        context.BindSignal(new SignalInfo(new StringName("load_end")));
        context.BindSignal(new SignalInfo(new StringName("load_error")));
        context.BindSignal(new SignalInfo(new StringName("eval_completed")));
        context.BindSignal(new SignalInfo(new StringName("bridge_request")));
        context.BindSignal(new SignalInfo(new StringName("new_window_requested")));
        context.BindSignal(new SignalInfo(new StringName("find_result")));
    }
}