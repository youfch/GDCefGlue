using Godot;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GDCefGlue.Demo;

/// <summary>
/// 测试用 C# 对象，通过 RegisterJavascriptObject 暴露给 JS。
/// 方法名自动 camelCase → JS 侧调用: window.dotnetBridge.methodName(args, callback)
/// </summary>
public class DotnetBridge
{
    public string Hello()
    {
        GD.Print("[DotnetBridge] Hello() called from JS");
        return "Hello from C#! 你好，世界！";
    }

    public string Echo(string message)
    {
        GD.Print($"[DotnetBridge] Echo() called from JS: \"{message}\"");
        return $"C# echoes: {message}";
    }

    public int Add(int a, int b)
    {
        var sum = a + b;
        GD.Print($"[DotnetBridge] Add({a}, {b}) = {sum}");
        return sum;
    }

    public string GetVersion()
    {
        GD.Print("[DotnetBridge] GetVersion() called from JS");
        return "GDCefGlue 1.0 + CefGlue 149";
    }
}

public partial class DemoScript : Control
{
    private CefGlueControl _browser;
    private RichTextLabel _log;

    public override void _Ready()
    {
        GD.Print("=== DemoScene _Ready ===");

        // 获取场景中已配置好的节点
        _browser = GetNode<CefGlueControl>("Browser");
        _log = GetNode<RichTextLabel>("LogPanel/Log");

        // 连接浏览器事件
        _browser.BrowserInitialized += OnBrowserReady;
        _browser.BridgeRequest += OnBridgeRequest;
        _browser.LoadEnd += OnLoadEnd;

// 链接工具栏按钮
        GetNode<Button>("Toolbar/ButtonRow/BtnEvalTitle").Pressed += () => OnEvalButton("document.title");
        GetNode<Button>("Toolbar/ButtonRow/BtnEvalUrl").Pressed += () => OnEvalButton("location.href");
        GetNode<Button>("Toolbar/ButtonRow/BtnEvalMath").Pressed += () => OnEvalButton("Math.PI * 2");
        GetNode<Button>("Toolbar/ButtonRow/BtnClearLog").Pressed += () => _log.Clear();
        GetNode<Button>("Toolbar/BtnEvalCustom").Pressed += () => OnCustomEval();
    }

    private void OnBrowserReady()
    {
        GD.Print("[Demo] Browser initialized");
        Log("浏览器已就绪");

        // 注册 C# 对象到 JS
        _browser.RegisterJavascriptObject(new DotnetBridge(), "dotnetBridge");
        GD.Print("[Demo] Registered 'dotnetBridge' — JS can call window.dotnetBridge.*");
        Log("已注册 dotnetBridge 对象，JS 可通过 window.dotnetBridge.* 调用");

        // 注入 _godotBridge 辅助脚本（兼容现有 godot:// bridge 机制）
        InjectBridgeScript();

        // 用 data: URI 加载测试 HTML
        LoadTestHtml();
    }

    private void InjectBridgeScript()
    {
        var js = @"
(function() {
    if (window._godotBridge) return;
    var pending = {};
    window._godotBridge = {
        _onMessage: function(m){},
        _onResponse: function(id,msg){
            if(pending[id]){ pending[id](msg); delete pending[id]; }
        }
    };
})();";
        _browser.ExecuteJavaScript(js);
    }

    private void LoadTestHtml()
    {
        // 从文件读取 HTML 并用 data: URI 加载
        var htmlPath = ProjectSettings.GlobalizePath("res://demo/ipc/test.html");
        if (!File.Exists(htmlPath))
        {
            GD.PrintErr($"[Demo] HTML not found at {htmlPath}");
            LogErr($"找不到 test.html: {htmlPath}");
            return;
        }

        var html = File.ReadAllText(htmlPath);
        var encoded = Uri.EscapeDataString(html);
        _browser.Address = "data:text/html;charset=utf-8," + encoded;
        GD.Print("[Demo] Loaded test.html via data: URI");
        Log("已加载测试页面");
    }

    private void OnLoadEnd(object sender, Xilium.CefGlue.Common.Events.LoadEndEventArgs e)
    {
        // 只在加载了真正的测试页后执行 eval 测试（跳过 about:blank 初始页）
        if (_browser.Address.StartsWith("data:text/html"))
        {
            _ = DelayedEvalTests();
        }
    }

    private async Task DelayedEvalTests()
    {
        await Task.Delay(1000);

        GD.Print("[Demo] === EvaluateJavaScript Tests ===");

        try
        {
            var title = await _browser.EvaluateJavaScript<string>("document.title");
            GD.Print($"[Demo] document.title = '{title}'");
            Log($"Eval: document.title = '{title}'");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Demo] Eval failed: {ex.Message}");
            LogErr($"Eval 失败: {ex.Message}");
        }

        try
        {
            var pi = await _browser.EvaluateJavaScript<double>("Math.PI");
            GD.Print($"[Demo] Math.PI = {pi}");
            Log($"Eval: Math.PI = {pi}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Demo] Eval failed: {ex.Message}");
        }

        // 测试 C# → JS 推送消息
        _browser.SendToJs("{\"type\":\"notification\",\"message\":\"Hello from C#!\"}");
        GD.Print("[Demo] Sent push message to JS");
        Log("C# → JS 推送消息已发送");
    }

    // ── 按钮事件 ────────────────────────────────────────────────

    private async void OnEvalButton(string jsCode)
    {
        GD.Print($"[Demo] Button: eval '{jsCode}'");
        Log($"→ 计算: {jsCode}");

        try
        {
            var result = await _browser.EvaluateJavaScript<string>(jsCode, timeout: TimeSpan.FromSeconds(5));
            GD.Print($"[Demo] Result: {result}");
            Log($"← {result}");
        }
        catch (TimeoutException)
        {
            GD.PrintErr("[Demo] Eval timed out");
            LogErr("超时");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Demo] Eval error: {ex.Message}");
            LogErr(ex.Message);
        }
    }

    private async void OnCustomEval()
    {
        var input = GetNode<LineEdit>("Toolbar/CustomCode");
        var code = input.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            code = "1 + 2 + 3";
            input.Text = code;
        }

        GD.Print($"[Demo] Custom eval: '{code}'");
        Log($"→ 自定义: {code}");

        try
        {
            var result = await _browser.EvaluateJavaScript<string>(code, timeout: TimeSpan.FromSeconds(5));
            GD.Print($"[Demo] Custom result: {result}");
            Log($"← {result}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Demo] Custom eval error: {ex.Message}");
            LogErr(ex.Message);
        }
    }

    // ── Bridge 事件 (JS → C# via godot://) ──────────────────────

    private void OnBridgeRequest(string type, string payload, string cbId)
    {
        GD.Print($"[Demo] BridgeRequest: type={type}, payload={payload}, cb={cbId ?? "(none)"}");
        Log($"← JS 桥接请求: {type}");

        switch (type)
        {
            case "ping":
                _browser.SendResponse(cbId, "{\"status\":\"pong\",\"from\":\"C#\"}");
                Log("→ 已回复 pong");
                break;

            case "status":
                var status = $"{{\"initialized\":true,\"loading\":{_browser.IsLoading.ToString().ToLower()}}}";
                _browser.SendResponse(cbId, status);
                Log("→ 已回复状态");
                break;

            default:
                _browser.SendResponse(cbId, $"{{\"error\":\"unknown type: {type}\"}}");
                break;
        }
    }

    // ── 日志 ─────────────────────────────────────────────────────

    private void Log(string msg)
    {
        // 可能从 CEF 线程调用 → 必须 CallDeferred 回 Godot 主线程
        CallDeferred(nameof(LogDeferred), $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
    }

    private void LogErr(string msg)
    {
        CallDeferred(nameof(LogErrDeferred), $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
    }

    private void LogDeferred(string text)
    {
        if (_log == null) return;
        _log.AddText(text);
        _log.ScrollToLine(int.MaxValue);
    }

    private void LogErrDeferred(string text)
    {
        if (_log == null) return;
        _log.PushColor(new Color(1, 0.4f, 0.3f));
        _log.AddText(text);
        _log.Pop();
        _log.ScrollToLine(int.MaxValue);
    }
}