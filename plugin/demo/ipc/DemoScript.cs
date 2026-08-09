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
    private readonly CefGlueControl _browser;

    public DotnetBridge(CefGlueControl browser)
    {
        _browser = browser;
    }

    public string Hello()
    {
        GD.Print("[DotnetBridge] Hello() called from JS");
        return "Hello from C#!";
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

    /// <summary>
    /// 从 JS 侧触发 EvaluateJavaScript，C# 执行 JS 代码并返回结果。
    /// JS 调用: window.dotnetBridge.eval('document.title').then(cb)
    ///
    /// 注意：这只是一个 IPC 往返的演示。实际使用时 JS 直接 eval() 即可，
    /// 不需要让 C# 来调 EvaluateJavaScript 再绕回来。
    /// </summary>
    public async Task<string> Eval(string code)
    {
        if (_browser == null)
            return null;
        try
        {
            var result = await _browser.EvaluateJavaScript<string>(code, timeout: TimeSpan.FromSeconds(5));
            GD.Print($"[DotnetBridge] Eval('{code}') = '{result}'");
            return result;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DotnetBridge] Eval failed: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    // ── 二进制通道 ──

    /// <summary>
    /// JS → C# 二进制数据。JS 传 Uint8Array 即可，CefGlue 框架自动做 base64 编码。
    /// C# 侧通过 DeserializeCallArgs 自动解码 B marker → byte[]。
    /// </summary>
    public void SendBinary(byte[] data)
    {
        _browser.RaiseBridgeBinary(data);
    }
}

public partial class DemoScript : Control
{
    private CefGlueControl _browser;
    private RichTextLabel _log;
    private bool _evalTestsStarted;

    public override void _Ready()
    {
        GD.Print("=== DemoScene _Ready ===");

        // 获取场景中已配置好的节点
        _browser = GetNode<CefGlueControl>("Browser");
        _log = GetNode<RichTextLabel>("LogPanel/Log");

        // 连接浏览器事件
        _browser.BrowserInitialized += OnBrowserReady;
        _browser.LoadEnd += OnLoadEnd;
        _browser.BridgeBinary += OnBinaryReceived;

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
        Log("Browser ready");

        // 注册 C# 对象到 JS — 在 OnLoadEnd 也会重新注册以确保导航后 V8 绑定可用
        RegisterBridgeObjects();

        // 用 data: URI 加载测试 HTML
        LoadTestHtml();
    }

    /// <summary>
    /// 注册 JS→C# bridge V8 对象。
    /// </summary>
    private void RegisterBridgeObjects()
    {
        _browser.RegisterJavascriptObject(new DotnetBridge(_browser), "dotnetBridge");
        GD.Print("[Demo] Registered 'dotnetBridge' — JS can call window.dotnetBridge.*");
        Log("dotnetBridge registered, JS can call window.dotnetBridge.*");
    }

    private void LoadTestHtml()
    {
        // 从文件读取 HTML 并用 data: URI 加载
        var htmlPath = ProjectSettings.GlobalizePath("res://demo/ipc/test.html");
        if (!File.Exists(htmlPath))
        {
            GD.PrintErr($"[Demo] HTML not found at {htmlPath}");
            LogErr($"test.html not found: {htmlPath}");
            return;
        }

        var html = File.ReadAllText(htmlPath);
        var encoded = Uri.EscapeDataString(html);
        _browser.Address = "data:text/html;charset=utf-8," + encoded;
        GD.Print("[Demo] Loaded test.html via data: URI");
        Log("Test page loaded");
    }

    private void OnLoadEnd(object sender, Xilium.CefGlue.Common.Events.LoadEndEventArgs e)
    {
        // 每次页面加载后重新注册 V8 绑定（BrowserProcess 的 V8 上下文重建可能失败）
        RegisterBridgeObjects();

        // 只在主帧且加载了测试页后执行自动化测试（防止子帧重复触发）
        if (_browser.Address.StartsWith("data:text/html") && !_evalTestsStarted)
        {
            _evalTestsStarted = true;
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
            LogErr($"Eval failed: {ex.Message}");
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
        Log("C# → JS push message sent");

        // 测试 C# → JS 二进制推送
        var binaryData = new byte[] { 0x47, 0x44, 0x43, 0x65, 0x66, 0x47, 0x6c, 0x75, 0x65 };
        _browser.SendBinaryToJs(binaryData);
        GD.Print("[Demo] Sent binary data to JS (9 bytes)");
        Log("C# → JS binary push sent");
    }

    // ── 按钮事件 ────────────────────────────────────────────────

    private async void OnEvalButton(string jsCode)
    {
        GD.Print($"[Demo] Button: eval '{jsCode}'");
        Log($"→ eval: {jsCode}");

        try
        {
            var result = await _browser.EvaluateJavaScript<string>(jsCode, timeout: TimeSpan.FromSeconds(5));
            GD.Print($"[Demo] Result: {result}");
            Log($"← {result}");
        }
        catch (TimeoutException)
        {
            GD.PrintErr("[Demo] Eval timed out");
            LogErr("Timeout");
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
        Log($"→ custom: {code}");

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

    // ── Bridge 事件 ──────────────────────────────────────────────

    private void OnBinaryReceived(byte[] data)
    {
        GD.Print($"[Demo] Binary received from JS: {data.Length} bytes");
        Log($"← Binary from JS: {data.Length} bytes, hex prefix: {BitConverter.ToString(data[..Math.Min(8, data.Length)])}");
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