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
        context.BindProperty(new PropertyInfo(new StringName(nameof(InitialUrl)), VariantType.String) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.InitialUrl, static (CefGlueControl i, string v) => i.InitialUrl = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(Mode)), VariantType.Int) { Hint = PropertyHint.Enum, HintString = "OSR,EmbeddedWindow", Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => (int)i.Mode, static (CefGlueControl i, int v) => i.Mode = (RenderMode)v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(FrameRate)), VariantType.Int) { Hint = PropertyHint.Range, HintString = "1,360", Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.FrameRate, static (CefGlueControl i, int v) => i.FrameRate = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(Transparent)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.Transparent, static (CefGlueControl i, bool v) => i.Transparent = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(Address)), VariantType.String) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.Address, static (CefGlueControl i, string v) => i.Address = v);

        context.AddPropertyGroup("Feature Toggles");
        context.BindProperty(new PropertyInfo(new StringName(nameof(GpuAcceleration)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.GpuAcceleration, static (CefGlueControl i, bool v) => i.GpuAcceleration = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(OpenPopupInCurrentBrowser)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.OpenPopupInCurrentBrowser, static (CefGlueControl i, bool v) => i.OpenPopupInCurrentBrowser = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(SyncCursor)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.SyncCursor, static (CefGlueControl i, bool v) => i.SyncCursor = v);
        context.BindProperty(new PropertyInfo(new StringName(nameof(EnableMediaStream)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.EnableMediaStream, static (CefGlueControl i, bool v) => i.EnableMediaStream = v);

        context.AddPropertyGroup("Embedded Mode");
        context.BindProperty(new PropertyInfo(new StringName(nameof(ForwardInputEvents)), VariantType.Bool) { Usage = PropertyUsageFlags.Default },
            static (CefGlueControl i) => i.ForwardInputEvents, static (CefGlueControl i, bool v) => i.ForwardInputEvents = v);
        // SyncCursor 在 EmbeddedWindow 模式下通过 _ValidateProperty 隐藏
        // ForwardInputEvents 在非 EmbeddedWindow 模式下通过 _ValidateProperty 隐藏

        context.BindMethod(new StringName(nameof(GoBack)), (CefGlueControl i) => i.GoBack());
        context.BindMethod(new StringName(nameof(GoForward)), (CefGlueControl i) => i.GoForward());
        context.BindMethod(new StringName(nameof(NavigateToUrl)), new ParameterInfo(new StringName("url"), VariantType.String),
            (CefGlueControl i, string url) => i.NavigateToUrl(url));
        context.BindMethod(new StringName(nameof(Reload)), new ParameterInfo(new StringName("ignoreCache"), VariantType.Bool, VariantTypeMetadata.None, Variant.CreateFrom(false)),
            (CefGlueControl i, bool ignoreCache) => i.Reload(ignoreCache));
        context.BindMethod(new StringName(nameof(ExecuteJavaScript)),
            new ParameterInfo(new StringName("code"), VariantType.String),
            new ParameterInfo(new StringName("url"), VariantType.String, VariantTypeMetadata.None, Variant.CreateFrom("about:blank")),
            new ParameterInfo(new StringName("line"), VariantType.Int, VariantTypeMetadata.Int32, Variant.CreateFrom(1)),
            (CefGlueControl i, string code, string url, int line) => i.ExecuteJavaScript(code, url, line));
        context.BindMethod(new StringName(nameof(EvalJs)), new ParameterInfo(new StringName("code"), VariantType.String),
            (CefGlueControl i, string code) => i.EvalJs(code));
        context.BindMethod(new StringName(nameof(RegisterJsHandler)),
            new ParameterInfo(new StringName("name"), VariantType.String),
            new ParameterInfo(new StringName("handler"), VariantType.Callable),
            new ParameterInfo(new StringName("methods"), VariantType.String, VariantTypeMetadata.None, Variant.CreateFrom("[\"hello\",\"echo\",\"add\",\"getVersion\",\"eval\"]")),
            (CefGlueControl i, string name, Callable handler, string methods) => i.RegisterJsHandler(name, handler, methods));
        context.BindMethod(new StringName(nameof(UnregisterJsHandler)), new ParameterInfo(new StringName("name"), VariantType.String),
            (CefGlueControl i, string name) => i.UnregisterJsHandler(name));
        context.BindMethod(new StringName(nameof(SendToJs)), new ParameterInfo(new StringName("json"), VariantType.String),
            (CefGlueControl i, string json) => i.SendToJs(json));
        context.BindMethod(new StringName(nameof(SendResponse)),
            new ParameterInfo(new StringName("cbId"), VariantType.String),
            new ParameterInfo(new StringName("json"), VariantType.String),
            (CefGlueControl i, string cbId, string json) => i.SendResponse(cbId, json));
        context.BindMethod(new StringName(nameof(ShowDeveloperTools)), (CefGlueControl i) => i.ShowDeveloperTools());
        context.BindMethod(new StringName(nameof(CloseDeveloperTools)), (CefGlueControl i) => i.CloseDeveloperTools());
        context.BindMethod(new StringName(nameof(OnEvalDone)),
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

        context.BindSignal(new SignalInfo(new StringName(nameof(BrowserInitialized))));
        context.BindSignal(new SignalInfo(new StringName(nameof(AddressChanged))));
        context.BindSignal(new SignalInfo(new StringName(nameof(TitleChanged))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadStart))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadEnd))));
        context.BindSignal(new SignalInfo(new StringName(nameof(LoadError))));
        context.BindSignal(new SignalInfo(new StringName("eval_completed")));
        context.BindSignal(new SignalInfo(new StringName("bridge_request")));
    }
}